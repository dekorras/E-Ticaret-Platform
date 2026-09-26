// Ürün sayfası konfigüratörü (spec 1.5). Sunucu tek doğruluk kaynağıdır: buradaki fiyat hesabı
// yalnızca ANLIK gösterim içindir (WallpaperPriceCalculator ile aynı formül), ardından
// POST /api/v1/pricing/quote cevabı ekrana yazılır; sepete eklerken fiyat sunucuda yeniden hesaplanır.
(function () {
    'use strict';

    var root = document.querySelector('[data-wall-configurator]');
    var dataEl = document.getElementById('wallConfigData');
    if (!root || !dataEl) return;

    var data = JSON.parse(dataEl.textContent);
    var product = data.product;
    var rules = data.rules;
    var materials = {};
    data.materials.forEach(function (m) { materials[m.code] = m; });

    var CM_PER = { cm: 1, m: 100, 'in': 2.54, ft: 30.48 };
    var isPattern = product.productType === 'Pattern';

    var state = {
        unit: data.initial.unit,
        wCm: data.initial.wCm,
        hCm: data.initial.hCm,
        material: data.initial.material,
        fit: data.initial.fit,
        mirror: !!data.initial.mirror,
        filter: data.initial.filter || 'none',
        crop: data.initial.crop,          // [x, y, w, h] normalize (0–1) veya null
        quantity: data.initial.quantity || 1,
        panelLines: true
    };

    var form = root.querySelector('[data-wall-form]');
    var widthInput = root.querySelector('[data-wall-width]');
    var heightInput = root.querySelector('[data-wall-height]');
    var canvas = root.querySelector('[data-wall-canvas]');
    var stage = root.querySelector('[data-wall-stage]');
    var fallback = root.querySelector('[data-wall-fallback]');
    var cropEditor = root.querySelector('[data-wall-crop-editor]');
    var cropImage = root.querySelector('[data-wall-crop-image]');
    var cropToggle = root.querySelector('[data-wall-crop-toggle]');
    var cropValue = root.querySelector('[data-wall-crop-value]');
    var dpiBox = root.querySelector('[data-wall-dpi]');
    var money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', minimumFractionDigits: 2 });
    var num2 = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    // ---------------- Ölçü ----------------
    function parseLength(text) {
        if (text == null) return NaN;
        var t = String(text).trim().replace(',', '.');
        if (t === '' || (t.match(/\./g) || []).length > 1 || !/^\d*\.?\d+$/.test(t)) return NaN;
        return parseFloat(t);
    }
    function round1(v) { return Math.round(v * 10) / 10; }
    function toCm(value, unit) { return round1(value * CM_PER[unit]); }
    function fromCm(cm, unit) {
        var d = unit === 'm' || unit === 'ft' ? 100 : 10;
        return Math.round(cm / CM_PER[unit] * d) / d;
    }
    function fmtInput(v) { return String(v).replace('.', ','); }

    function validate() {
        var errors = {};
        var m = materials[state.material];
        if (!(state.wCm >= rules.minSideCm)) errors.width = 'En en az ' + rules.minSideCm + ' cm olmalıdır.';
        else if (state.wCm > rules.maxWidthCm) errors.width = 'En en fazla ' + rules.maxWidthCm + ' cm olabilir.';
        if (!(state.hCm >= rules.minSideCm)) errors.height = 'Boy en az ' + rules.minSideCm + ' cm olmalıdır.';
        else if (m && state.hCm > m.maxHeightCm) errors.height = 'Seçilen malzemede boy en fazla ' + m.maxHeightCm + ' cm olabilir.';
        return errors;
    }

    function showErrors(errors) {
        ['width', 'height', 'material', 'quantity', 'product'].forEach(function (key) {
            var el = root.querySelector('[data-wall-error="' + key + '"]');
            if (el) el.textContent = errors[key] || '';
        });
        widthInput.classList.toggle('is-invalid', !!errors.width);
        heightInput.classList.toggle('is-invalid', !!errors.height);
        widthInput.setAttribute('aria-invalid', errors.width ? 'true' : 'false');
        heightInput.setAttribute('aria-invalid', errors.height ? 'true' : 'false');
    }

    function readDimensions() {
        var w = parseLength(widthInput.value);
        var h = parseLength(heightInput.value);
        state.wCm = isNaN(w) ? NaN : toCm(w, state.unit);
        state.hCm = isNaN(h) ? NaN : toCm(h, state.unit);
    }

    // ---------------- Anlık fiyat (sunucu formülünün aynısı) ----------------
    function localQuote() {
        var m = materials[state.material];
        if (!m || isNaN(state.wCm) || isNaN(state.hCm)) return null;
        var pw = state.wCm + m.bleedCm, ph = state.hCm + m.bleedCm;
        var area = state.wCm * state.hCm / 10000;
        var billed = Math.max(rules.chargeBleed ? pw * ph / 10000 : area, m.minBillableAreaM2);
        var unit = Math.round(billed * m.pricePerM2 * 100 + 1e-9) / 100;
        return { area: area, billed: billed, panels: Math.ceil(pw / m.panelWidthCm), panelWidth: m.panelWidthCm, unit: unit, subtotal: unit * state.quantity };
    }

    function setText(sel, text) { var el = root.querySelector(sel); if (el) el.textContent = text; }
    function setHidden(sel, hidden) { root.querySelectorAll(sel).forEach(function (el) { el.hidden = hidden; }); }

    function renderSummary(q, server) {
        if (!q) return;
        setText('[data-wall-area]', num2.format(q.area) + ' m²');
        setText('[data-sum="billed"]', num2.format(q.billed) + ' m²');
        setText('[data-sum="panels"]', q.panels + ' × ' + q.panelWidth + ' cm');
        setText('[data-sum="unit"]', money.format(q.unit));
        setText('[data-sum="subtotal"]', money.format(q.subtotal));
        if (!server) {
            var tax = Math.round(q.subtotal * product.taxRatePercentage) / 100;
            setText('[data-sum="tax"]', money.format(tax));
            setText('[data-sum="total"]', money.format(q.subtotal + tax));
            return;
        }
        setHidden('[data-sum-row="discount"], [data-sum="discount"]', !(server.indirim > 0));
        setText('[data-sum="discount"]', '-' + money.format(server.indirim));
        setText('[data-sum="tax"]', money.format(server.kdv));
        setText('[data-sum="total"]', money.format(server.toplam));

        var shipping = root.querySelector('[data-sum="shipping"]');
        if (shipping) {
            shipping.innerHTML = server.kargo === 0
                ? '<span class="text-success">Kargo ücretsiz.</span>'
                : (server.ucretsizKargoIcinKalan > 0
                    ? 'Ücretsiz kargoya <strong>' + money.format(server.ucretsizKargoIcinKalan) + '</strong> kaldı.'
                    : '<span class="text-muted">Kargo ücreti ödeme adımında hesaplanır.</span>');
        }
        var glue = root.querySelector('[data-sum="glue"]');
        if (glue) glue.textContent = server.tutkalGerekir ? ('Bu malzeme tutkal gerektirir' + (server.tutkalUcretsiz ? ' — tutkal ücretsiz.' : '.')) : '';
        if (server.tahminiTeslimEnErken) setText('[data-sum="delivery"]', formatRange(server.tahminiTeslimEnErken, server.tahminiTeslimEnGec));
    }

    function formatRange(a, b) {
        var da = new Date(a + 'T00:00:00'), db = new Date(b + 'T00:00:00');
        var month = function (d) { return d.toLocaleDateString('tr-TR', { month: 'long' }); };
        return da.getMonth() === db.getMonth()
            ? da.getDate() + '–' + db.getDate() + ' ' + month(db)
            : da.getDate() + ' ' + month(da) + ' – ' + db.getDate() + ' ' + month(db);
    }

    var quoteTimer = null, quoteSeq = 0;
    function requestQuote() {
        clearTimeout(quoteTimer);
        quoteTimer = setTimeout(function () {
            var seq = ++quoteSeq;
            fetch('/api/v1/pricing/quote', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({ product: product.slug, configuration: configPayload(), quantity: state.quantity })
            }).then(function (r) { return r.json().then(function (d) { return { ok: r.ok, data: d }; }); })
              .then(function (res) {
                  if (seq !== quoteSeq) return;
                  if (!res.ok) { showErrors(flattenErrors(res.data)); return; }
                  if (!res.data.gecerli) { showErrors(res.data.hatalar || {}); return; }
                  var d = res.data;
                  renderSummary({ area: d.alanM2, billed: d.faturaM2, panels: d.panelSayisi, panelWidth: d.panelGenisligi, unit: d.birimFiyat, subtotal: d.araToplam }, d);
              }).catch(function () { /* ağ hatası: anlık tahmin ekranda kalır */ });
        }, 300);
    }

    function flattenErrors(problem) {
        var out = {};
        if (problem && problem.errors) Object.keys(problem.errors).forEach(function (k) { out[k] = [].concat(problem.errors[k]).join(' '); });
        return out;
    }

    // ---------------- Konfigürasyon / URL ----------------
    function cropQuery() {
        return state.crop ? state.crop.map(function (v) { return (Math.round(v * 10000) / 10000).toString(); }).join(',') : null;
    }

    function configPayload() {
        var p = { material: state.material, unit: state.unit, w_cm: String(state.wCm), h_cm: String(state.hCm), fit: state.fit, mirror: state.mirror ? '1' : '0', filter: state.filter };
        if (state.fit === 'crop' && state.crop) p.crop = cropQuery();
        return p;
    }

    function configQuery() {
        var params = new URLSearchParams();
        params.set('slug', product.slug);
        params.set('view', 'customizer');
        params.set('material', state.material);
        params.set('unit', state.unit);
        params.set('w_cm', state.wCm.toFixed(1));
        params.set('h_cm', state.hCm.toFixed(1));
        params.set('fit', state.fit);
        if (state.mirror) params.set('mirror', '1');
        if (state.filter !== 'none') params.set('filter', state.filter);
        if (state.fit === 'crop' && state.crop) params.set('crop', cropQuery());
        if (state.quantity > 1) params.set('qty', String(state.quantity));
        return params;
    }

    function syncUrl() {
        if (isNaN(state.wCm) || isNaN(state.hCm)) return;
        var params = configQuery();
        history.replaceState(history.state, '', location.pathname + '?' + params.toString());
        if (cropValue) cropValue.value = state.fit === 'crop' && state.crop ? cropQuery() : '';
        updatePreviewLinks(params);
    }

    // "Duvarında Gör" bağlantıları o anki konfigürasyonla açılsın (spec 1.6.6-B).
    function updatePreviewLinks(params) {
        document.querySelectorAll('a[data-wall-preview="' + product.slug + '"]').forEach(function (a) {
            var url = new URL(a.getAttribute('href'), location.origin);
            ['material', 'unit', 'w_cm', 'h_cm', 'fit', 'mirror', 'filter', 'crop'].forEach(function (k) { url.searchParams.delete(k); });
            params.forEach(function (v, k) { if (k !== 'slug' && k !== 'view' && k !== 'qty') url.searchParams.set(k, v); });
            url.searchParams.set('return', location.pathname + location.search);
            a.setAttribute('href', url.pathname + url.search);
        });
    }

    // ---------------- Önizleme ----------------
    var img = new Image();
    var imgReady = false;
    img.decoding = 'async';
    img.onload = function () { imgReady = true; if (fallback) fallback.hidden = true; if (!state.crop && !isPattern) state.crop = defaultCrop(); draw(); updateDpi(); };
    img.onerror = function () { if (fallback) fallback.hidden = false; };
    if (product.previewUrl) img.src = product.previewUrl;

    var supportsCtxFilter = (function () { try { return typeof document.createElement('canvas').getContext('2d').filter === 'string'; } catch (e) { return false; } })();

    function targetRatio() { return state.wCm / state.hCm; }

    function defaultCrop() {
        // Hedef orana uyan, görsele sığan en büyük ortalanmış alan.
        var ir = img.naturalWidth / img.naturalHeight, tr = targetRatio();
        if (!(tr > 0) || !(ir > 0)) return [0, 0, 1, 1];
        return tr > ir ? [0, (1 - ir / tr) / 2, 1, ir / tr] : [(1 - tr / ir) / 2, 0, tr / ir, 1];
    }

    /** Kırpma alanını yeni orana uyarla (ölçü değişince): merkez ve YAKINLIK (varsayılan alana göre ölçek) korunur.
     *  Önceki sürüm mutlak genişliği koruyordu; ölçü yazılırken oluşan uç ara oranlar ("4" → "40" → "400") alanı
     *  bir şeride küçültüyor ve bir daha büyümüyordu → görsel aşırı yakınlaşıp bozuluyordu. */
    function adaptCrop() {
        if (!state.crop || !imgReady || !(targetRatio() > 0) || !isFinite(targetRatio())) return;
        var iw = img.naturalWidth, ih = img.naturalHeight, c = state.crop;
        var prevRatio = (c[2] * iw) / (c[3] * ih);
        var prevDef = cropFor(prevRatio);
        var s = Math.min(1, Math.max(0.05, c[2] / prevDef[2]));
        var def = defaultCrop();
        var w = def[2] * s, h = def[3] * s;
        var cx = c[0] + c[2] / 2, cy = c[1] + c[3] / 2;
        state.crop = [Math.min(Math.max(cx - w / 2, 0), 1 - w), Math.min(Math.max(cy - h / 2, 0), 1 - h), w, h];
    }
    function cropFor(tr) {
        var ir = img.naturalWidth / img.naturalHeight;
        return tr > ir ? [0, (1 - ir / tr) / 2, 1, ir / tr] : [(1 - tr / ir) / 2, 0, tr / ir, 1];
    }

    function draw() {
        if (isNaN(state.wCm) || isNaN(state.hCm) || state.wCm <= 0 || state.hCm <= 0) return;
        var ratio = targetRatio();
        stage.style.setProperty('--wall-ratio', String(ratio));
        var cssWidth = stage.clientWidth || 600;
        var maxHeight = Math.min(560, window.innerHeight * 0.7);
        var width = cssWidth, height = width / ratio;
        if (height > maxHeight) { height = maxHeight; width = height * ratio; }
        var dpr = Math.min(window.devicePixelRatio || 1, 2);
        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        canvas.style.width = Math.round(width) + 'px';
        canvas.style.height = Math.round(height) + 'px';
        setText('[data-wall-stage-caption]', fromCmLabel(state.wCm) + ' × ' + fromCmLabel(state.hCm) + ' · kesikli çizgiler panel ek yerleridir');
        if (!imgReady) return;

        var ctx = canvas.getContext('2d');
        ctx.save();
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        if (state.mirror) { ctx.translate(canvas.width, 0); ctx.scale(-1, 1); }
        if (supportsCtxFilter) ctx.filter = state.filter === 'grayscale' ? 'grayscale(1)' : state.filter === 'sepia' ? 'sepia(1)' : 'none';

        if (isPattern && product.repeatWidthCm > 0 && product.repeatHeightCm > 0) {
            drawPattern(ctx);
        } else if (state.fit === 'stretch') {
            ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
        } else {
            var c = state.crop || defaultCrop();
            ctx.drawImage(img, c[0] * img.naturalWidth, c[1] * img.naturalHeight, c[2] * img.naturalWidth, c[3] * img.naturalHeight, 0, 0, canvas.width, canvas.height);
        }
        ctx.restore();
        if (!supportsCtxFilter && state.filter !== 'none') applyManualFilter(ctx);
        if (state.panelLines) drawPanelLines(ctx);
    }

    function fromCmLabel(cm) { return fmtInput(fromCm(cm, state.unit)) + ' ' + (state.unit === 'in' ? 'inç' : state.unit); }

    function drawPattern(ctx) {
        // WallLayout.TilePattern ile aynı kural: HalfDrop'ta tek sütunlar yarım tekrar yukarı kayar.
        var scale = canvas.width / state.wCm;
        var tw = product.repeatWidthCm * scale, th = product.repeatHeightCm * scale;
        var cols = Math.ceil(canvas.width / tw);
        for (var col = 0; col < cols; col++) {
            var offset = product.repeatType === 'HalfDrop' && col % 2 === 1 ? -th / 2 : 0;
            for (var y = offset; y < canvas.height; y += th) ctx.drawImage(img, col * tw, y, tw, th);
        }
    }

    function applyManualFilter(ctx) {
        // Safari gibi ctx.filter desteklemeyen tarayıcılar için piksel bazlı yedek.
        var frame = ctx.getImageData(0, 0, canvas.width, canvas.height), d = frame.data;
        for (var i = 0; i < d.length; i += 4) {
            var r = d[i], g = d[i + 1], b = d[i + 2];
            if (state.filter === 'grayscale') {
                var l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                d[i] = d[i + 1] = d[i + 2] = l;
            } else {
                d[i] = Math.min(255, 0.393 * r + 0.769 * g + 0.189 * b);
                d[i + 1] = Math.min(255, 0.349 * r + 0.686 * g + 0.168 * b);
                d[i + 2] = Math.min(255, 0.272 * r + 0.534 * g + 0.131 * b);
            }
        }
        ctx.putImageData(frame, 0, 0);
    }

    function drawPanelLines(ctx) {
        var m = materials[state.material];
        if (!m) return;
        // VARSAYIM: kesim payı iki kenara eşit dağıtılır; ek yeri = k × panel eni − pay/2 (müşteri ölçüsünde).
        var scale = canvas.width / state.wCm;
        ctx.save();
        ctx.lineWidth = Math.max(1, canvas.width / 600);
        ctx.setLineDash([8 * ctx.lineWidth, 6 * ctx.lineWidth]);
        ctx.font = (11 * (canvas.width / (canvas.clientWidth || canvas.width))) + 'px sans-serif';
        var panel = 1, lastX = 0;
        for (var x = m.panelWidthCm - m.bleedCm / 2; x < state.wCm; x += m.panelWidthCm) {
            var px = Math.round(x * scale) + 0.5;
            ctx.strokeStyle = 'rgba(255,255,255,.9)';
            ctx.beginPath(); ctx.moveTo(px, 0); ctx.lineTo(px, canvas.height); ctx.stroke();
            ctx.strokeStyle = 'rgba(0,0,0,.55)';
            ctx.lineDashOffset = 7 * ctx.lineWidth;
            ctx.beginPath(); ctx.moveTo(px, 0); ctx.lineTo(px, canvas.height); ctx.stroke();
            ctx.lineDashOffset = 0;
            label(ctx, panel++, (lastX + px) / 2);
            lastX = px;
        }
        if (panel > 1) label(ctx, panel, (lastX + canvas.width) / 2);
        ctx.restore();
    }

    function label(ctx, n, x) {
        ctx.fillStyle = 'rgba(0,0,0,.55)';
        var s = parseFloat(ctx.font) || 11;
        ctx.fillRect(x - s, 4, s * 2, s * 1.5);
        ctx.fillStyle = '#fff';
        ctx.textAlign = 'center';
        ctx.fillText(String(n), x, 4 + s * 1.1);
    }

    // ---------------- Çözünürlük uyarısı ----------------
    function updateDpi() {
        if (!dpiBox || !imgReady || isNaN(state.wCm) || isNaN(state.hCm)) return;
        // Orijinal görsel boyutu biliniyorsa o, yoksa önizleme görselinin boyutu (yaklaşık) kullanılır.
        var known = product.imageWidthPx > 0 && product.imageHeightPx > 0;
        var srcW = known ? product.imageWidthPx : img.naturalWidth;
        var srcH = known ? product.imageHeightPx : img.naturalHeight;
        var dpi;
        if (isPattern && product.repeatWidthCm > 0) {
            dpi = Math.min(srcW / (product.repeatWidthCm / 2.54), srcH / (product.repeatHeightCm / 2.54));
        } else {
            var c = state.fit === 'stretch' ? [0, 0, 1, 1] : (state.crop || defaultCrop());
            dpi = Math.min(c[2] * srcW / (state.wCm / 2.54), c[3] * srcH / (state.hCm / 2.54));
        }
        var low = dpi < rules.minPrintDpi;
        dpiBox.hidden = !low;
        if (low) {
            dpiBox.innerHTML = '<i class="bi bi-exclamation-triangle"></i> Baskı kalitesi düşebilir: bu ölçüde görselin efektif çözünürlüğü ' +
                (known ? '' : 'yaklaşık ') + Math.round(dpi) + ' DPI (önerilen en az ' + rules.minPrintDpi + ' DPI). Ölçüyü küçültmeyi veya daha geniş bir kırpma alanı seçmeyi deneyin.';
        }
    }

    // ---------------- Kırpma (Cropper.js) ----------------
    var cropper = null;
    function openCropEditor() {
        if (!window.Cropper || !imgReady || isPattern) return;
        if (state.fit !== 'crop') setFit('crop');
        cropEditor.hidden = false;
        canvas.hidden = true;
        cropToggle.setAttribute('aria-expanded', 'true');
        cropToggle.textContent = 'Kırpmayı bitir';
        setHidden('[data-wall-crop-help]', false);
        cropImage.src = img.src;
        var c = state.crop || defaultCrop();
        cropper = new window.Cropper(cropImage, {
            aspectRatio: targetRatio(),
            viewMode: 1,
            autoCropArea: 1,
            zoomable: false,
            rotatable: false,
            scalable: false,
            movable: false,
            background: false,
            ready: function () {
                cropper.setData({ x: c[0] * img.naturalWidth, y: c[1] * img.naturalHeight, width: c[2] * img.naturalWidth, height: c[3] * img.naturalHeight });
                var box = cropEditor.querySelector('.cropper-crop-box');
                if (box) { box.tabIndex = 0; box.setAttribute('role', 'slider'); box.setAttribute('aria-label', 'Kırpma alanı - ok tuşlarıyla taşıyın'); box.focus(); }
            },
            cropend: readCropper
        });
    }

    function readCropper() {
        if (!cropper) return;
        var d = cropper.getData(true);
        var W = img.naturalWidth, H = img.naturalHeight;
        state.crop = [Math.max(0, d.x / W), Math.max(0, d.y / H), Math.min(1, d.width / W), Math.min(1, d.height / H)];
        updateDpi();
        syncUrl();
    }

    function closeCropEditor() {
        if (cropper) { readCropper(); cropper.destroy(); cropper = null; }
        cropEditor.hidden = true;
        canvas.hidden = false;
        cropToggle.setAttribute('aria-expanded', 'false');
        cropToggle.textContent = 'Kırpma alanını ayarla';
        setHidden('[data-wall-crop-help]', true);
        draw();
    }

    cropEditor && cropEditor.addEventListener('keydown', function (e) {
        if (!cropper || ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].indexOf(e.key) < 0) return;
        e.preventDefault();
        var step = (e.shiftKey ? 0.05 : 0.01);
        var d = cropper.getData();
        var dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0;
        var dy = e.key === 'ArrowUp' ? -step : e.key === 'ArrowDown' ? step : 0;
        cropper.setData({ x: d.x + dx * img.naturalWidth, y: d.y + dy * img.naturalHeight });
        readCropper();
    });
    if (cropToggle) cropToggle.addEventListener('click', function () { if (cropper) closeCropEditor(); else openCropEditor(); });

    // ---------------- Olaylar ----------------
    function update(opts) {
        opts = opts || {};
        var errors = validate();
        showErrors(errors);
        var hasErrors = Object.keys(errors).length > 0;
        root.querySelector('[data-wall-add]').disabled = hasErrors;
        if (!hasErrors) {
            if (opts.ratioChanged) adaptCrop();
            if (cropper) { cropper.setAspectRatio(targetRatio()); readCropper(); }
            renderSummary(localQuote(), null);
            requestQuote();
            syncUrl();
            draw();
            updateDpi();
        }
    }

    function setFit(fit) {
        state.fit = fit;
        var input = root.querySelector('[data-wall-fit][value="' + fit + '"]');
        if (input) input.checked = true;
        setHidden('[data-wall-stretch-warning]', fit !== 'stretch');
        if (fit === 'stretch' && cropper) closeCropEditor();
        if (fit === 'crop' && !state.crop && imgReady) state.crop = defaultCrop();
    }

    widthInput.addEventListener('input', function () { readDimensions(); update({ ratioChanged: true }); });
    heightInput.addEventListener('input', function () { readDimensions(); update({ ratioChanged: true }); });

    root.querySelectorAll('[data-wall-unit]').forEach(function (radio) {
        radio.addEventListener('change', function () {
            // Birim değişince değerler dönüştürülerek korunur (sıfırlanmaz) - spec 1.3.
            state.unit = radio.value;
            if (!isNaN(state.wCm)) widthInput.value = fmtInput(fromCm(state.wCm, state.unit));
            if (!isNaN(state.hCm)) heightInput.value = fmtInput(fromCm(state.hCm, state.unit));
            root.querySelectorAll('[data-wall-unit-label]').forEach(function (el) { el.textContent = state.unit === 'in' ? 'inç' : state.unit; });
            update();
        });
    });

    root.querySelectorAll('[data-wall-material]').forEach(function (radio) {
        radio.addEventListener('change', function () {
            state.material = radio.value;
            var m = materials[state.material];
            setText('[data-wall-bleed-info]', 'Kesim/montaj için her ölçüye ' + m.bleedCm + ' cm pay ekliyoruz' + (rules.chargeBleed ? ' (kesim payı fiyata dahildir).' : '.'));
            var sample = root.querySelector('[data-wall-sample]');
            if (sample) sample.hidden = !m.sampleProductId;
            update();
        });
    });

    root.querySelectorAll('[data-wall-fit]').forEach(function (radio) {
        radio.addEventListener('change', function () { setFit(radio.value); update(); });
    });
    var mirror = root.querySelector('[data-wall-mirror]');
    if (mirror) mirror.addEventListener('change', function () { state.mirror = mirror.checked; update(); });
    root.querySelectorAll('[data-wall-filter]').forEach(function (radio) {
        radio.addEventListener('change', function () { state.filter = radio.value; update(); });
    });
    var qty = root.querySelector('[data-wall-qty]');
    if (qty) qty.addEventListener('input', function () {
        var v = parseInt(qty.value, 10);
        state.quantity = v >= 1 && v <= 99 ? v : 1;
        update();
    });
    var panelToggle = root.querySelector('[data-wall-panel-lines]');
    if (panelToggle) panelToggle.addEventListener('change', function () { state.panelLines = panelToggle.checked; draw(); });

    var copyBtn = root.querySelector('[data-wall-copy-link]');
    if (copyBtn) copyBtn.addEventListener('click', function () {
        var url = location.origin + location.pathname + '?' + configQuery().toString();
        var done = function () { window.DekorrasWall && window.DekorrasWall.toast('Bağlantı kopyalandı.'); };
        if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(url).then(done, function () { window.prompt('Bağlantıyı kopyalayın:', url); });
        else window.prompt('Bağlantıyı kopyalayın:', url);
    });

    var sampleBtn = root.querySelector('[data-wall-sample]');
    if (sampleBtn) sampleBtn.addEventListener('click', function () {
        var m = materials[state.material];
        if (!m.sampleProductId) return;
        var f = document.createElement('form');
        f.method = 'post';
        f.action = '/Cart/Add';
        var token = form.querySelector('input[name="__RequestVerificationToken"]');
        [['productId', m.sampleProductId], ['quantity', '1'], ['__RequestVerificationToken', token ? token.value : '']].forEach(function (pair) {
            var i = document.createElement('input'); i.type = 'hidden'; i.name = pair[0]; i.value = pair[1]; f.appendChild(i);
        });
        document.body.appendChild(f);
        f.submit();
    });

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        readDimensions();
        var errors = validate();
        showErrors(errors);
        if (Object.keys(errors).length) return;
        var button = root.querySelector('[data-wall-add]');
        button.disabled = true;
        fetch('/api/v1/cart/items', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify({ product: product.slug, configuration: configPayload(), quantity: state.quantity })
        }).then(function (r) { return r.json().catch(function () { return {}; }).then(function (d) { return { ok: r.ok, data: d }; }); })
          .then(function (res) {
              if (!res.ok) { showErrors(flattenErrors(res.data)); throw new Error(res.data.title || 'Sepete eklenemedi.'); }
              document.dispatchEvent(new CustomEvent('wallpreview:add-to-cart', { detail: { product: product.slug, configuration: configPayload() } }));
              var toast = document.getElementById('wallAddedToast');
              if (!toast) {
                  toast = document.createElement('div');
                  toast.id = 'wallAddedToast';
                  toast.className = 'alert alert-success d-flex align-items-center gap-2 mt-2';
                  toast.setAttribute('role', 'status');
                  form.querySelector('[data-wall-add]').closest('.wall-step').appendChild(toast);
              }
              toast.innerHTML = '<i class="bi bi-check-circle"></i> Sepete eklendi. <a class="ms-auto btn btn-sm btn-success" href="/Cart">Sepete git</a>';
          })
          .catch(function (err) { window.DekorrasWall && window.DekorrasWall.toast(err.message, true); })
          .finally(function () { button.disabled = false; });
    });

    // Görüntüleyiciden (aynı sayfa modalı) dönen konfigürasyonu uygula (spec 1.6.6-C).
    document.addEventListener('wallpreview:apply', function (e) {
        var c = e.detail || {};
        if (c.product && c.product !== product.slug) return;
        if (c.unit && CM_PER[c.unit]) { state.unit = c.unit; var u = root.querySelector('[data-wall-unit][value="' + c.unit + '"]'); if (u) u.checked = true; }
        if (c.w_cm) state.wCm = round1(parseFloat(c.w_cm));
        if (c.h_cm) state.hCm = round1(parseFloat(c.h_cm));
        if (c.material && materials[c.material]) { state.material = c.material; var mi = root.querySelector('[data-wall-material][value="' + c.material + '"]'); if (mi) mi.checked = true; }
        if (c.fit) setFit(c.fit === 'stretch' ? 'stretch' : 'crop');
        if (c.mirror != null) { state.mirror = c.mirror === true || c.mirror === '1'; if (mirror) mirror.checked = state.mirror; }
        if (c.filter) { state.filter = c.filter; var fi = root.querySelector('[data-wall-filter][value="' + c.filter + '"]'); if (fi) fi.checked = true; }
        if (c.crop) { var parts = String(c.crop).split(',').map(parseFloat); if (parts.length === 4 && parts.every(isFinite)) state.crop = parts; }
        widthInput.value = fmtInput(fromCm(state.wCm, state.unit));
        heightInput.value = fmtInput(fromCm(state.hCm, state.unit));
        root.querySelectorAll('[data-wall-unit-label]').forEach(function (el) { el.textContent = state.unit === 'in' ? 'inç' : state.unit; });
        update();
    });

    // Diğer modüllere (Duvarımda Dene, görüntüleyici) o anki konfigürasyonu ver.
    if (window.DekorrasWall) {
        window.DekorrasWall.currentConfiguration = function (productId) {
            return productId === product.productId ? configPayload() : null;
        };
    }
    window.DekorrasConfigurator = { slug: product.slug, getConfiguration: configPayload, getState: function () { return Object.assign({}, state); } };

    var resizeTimer = null;
    window.addEventListener('resize', function () { clearTimeout(resizeTimer); resizeTimer = setTimeout(draw, 150); });

    setFit(state.fit);
    update();
})();
