// "Kendi odamı yükle" (spec 1.6.4): 1) fotoğraf seç 2) duvarın 4 köşesini sürükle + gerçek duvar
// genişliği 3) (isteğe bağlı) önündeki mobilyayı fırçayla boya → ön plan maskesi. Fotoğraf sunucuda
// içerikten doğrulanır, EXIF temizlenir ve yeniden kodlanır; kullanıcının özel sahnesi olarak kaydedilir.
(function () {
    'use strict';

    var MAX_BYTES = 10 * 1024 * 1024;
    var LABELS = ['Sol üst', 'Sağ üst', 'Sağ alt', 'Sol alt'];

    function token() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function send(url, formData) {
        return fetch(url, { method: 'POST', body: formData, credentials: 'same-origin', headers: { 'RequestVerificationToken': token(), 'Accept': 'application/json' } })
            .then(function (r) {
                return r.json().catch(function () { return {}; }).then(function (d) {
                    if (!r.ok) {
                        var msg = r.status === 429 ? 'Çok sık yükleme yaptınız; lütfen biraz sonra tekrar deneyin.' : (d.detail || d.title || 'Yükleme başarısız.');
                        if (d.errors) msg = Object.keys(d.errors).map(function (k) { return [].concat(d.errors[k]).join(' '); }).join(' ');
                        throw new Error(msg);
                    }
                    return d;
                });
            });
    }

    // =====================================================================
    // Duvarın önündeki eşyaları otomatik bulma (tarayıcıda, sunucuya gönderilmeden)
    // Duvarın renkleri, işaretlenen duvar dörtgeninin içindeki en sık renklerden öğrenilir; bu renklerden belirgin
    // biçimde ayrılan, yeterince büyük ve bütünlüklü bölgeler "eşya" sayılır. Yalnızca dörtgen içi önemlidir:
    // poster yalnızca orada çizilir. VARSAYIM: duvar büyük ölçüde düz/az desenli; yoğun desenli duvarda
    // (kapsama > %55) sonuç güvenilmez sayılıp uygulanmaz ve kullanıcıya elle işaretlemesi söylenir.
    // =====================================================================
    function morph(src, w, h, r, dilate) {
        var tmp = new Uint8Array(w * h), out = new Uint8Array(w * h), x, y, k, v;
        for (y = 0; y < h; y++) for (x = 0; x < w; x++) {
            v = dilate ? 0 : 1;
            for (k = -r; k <= r; k++) {
                var xx = x + k;
                if (xx < 0 || xx >= w) continue;
                if (dilate && src[y * w + xx]) { v = 1; break; }
                if (!dilate && !src[y * w + xx]) { v = 0; break; }
            }
            tmp[y * w + x] = v;
        }
        for (y = 0; y < h; y++) for (x = 0; x < w; x++) {
            v = dilate ? 0 : 1;
            for (k = -r; k <= r; k++) {
                var yy = y + k;
                if (yy < 0 || yy >= h) continue;
                if (dilate && tmp[yy * w + x]) { v = 1; break; }
                if (!dilate && !tmp[yy * w + x]) { v = 0; break; }
            }
            out[y * w + x] = v;
        }
        return out;
    }

    function components(bin, allowed, w, h, visit) {
        var label = new Int32Array(w * h), stack = new Int32Array(w * h), next = 0;
        for (var start = 0; start < w * h; start++) {
            if (!bin[start] || !allowed[start] || label[start]) continue;
            next++;
            var sp = 0, pixels = [];
            stack[sp++] = start; label[start] = next;
            while (sp) {
                var i = stack[--sp];
                pixels.push(i);
                var x = i % w, y = (i / w) | 0;
                var nb = [x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1];
                for (var n = 0; n < 4; n++) {
                    var j = nb[n];
                    if (j >= 0 && bin[j] && allowed[j] && !label[j]) { label[j] = next; stack[sp++] = j; }
                }
            }
            visit(pixels);
        }
    }

    function detectForeground(img, quad, natW, natH) {
        var scale = Math.min(1, 640 / Math.max(natW, natH));
        var w = Math.max(1, Math.round(natW * scale)), h = Math.max(1, Math.round(natH * scale));
        var c = document.createElement('canvas'); c.width = w; c.height = h;
        var ctx = c.getContext('2d', { willReadFrequently: true });
        ctx.drawImage(img, 0, 0, w, h);
        var px = ctx.getImageData(0, 0, w, h).data;

        var qc = document.createElement('canvas'); qc.width = w; qc.height = h;
        var qctx = qc.getContext('2d', { willReadFrequently: true });
        qctx.fillStyle = '#fff';
        qctx.beginPath();
        quad.forEach(function (p, i) { if (i === 0) qctx.moveTo(p[0] * scale, p[1] * scale); else qctx.lineTo(p[0] * scale, p[1] * scale); });
        qctx.closePath(); qctx.fill();
        var qa = qctx.getImageData(0, 0, w, h).data;
        var inQuad = new Uint8Array(w * h), n = 0, i;
        for (i = 0; i < w * h; i++) if (qa[i * 4 + 3] > 127) { inQuad[i] = 1; n++; }
        if (n < 200) return null;

        // 1) Duvar renk modeli: dörtgen içi renklerin 16 seviyeli histogramında en sık kovalar (piksellerin %70'i).
        var count = new Float64Array(4096), sr = new Float64Array(4096), sg = new Float64Array(4096), sb = new Float64Array(4096);
        for (i = 0; i < w * h; i++) {
            if (!inQuad[i]) continue;
            var r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2], key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
            count[key]++; sr[key] += r; sg[key] += g; sb[key] += b;
        }
        var keys = [];
        for (i = 0; i < 4096; i++) if (count[i]) keys.push(i);
        keys.sort(function (a, b) { return count[b] - count[a]; });
        var wall = [], cum = 0;
        for (i = 0; i < keys.length && cum < n * 0.7 && wall.length < 96; i++) {
            var k = keys[i];
            wall.push([sr[k] / count[k], sg[k] / count[k], sb[k] / count[k]]);
            cum += count[k];
        }

        // 2) Duvar renklerinin hiçbirine yakın olmayan pikseller aday eşya.
        var T2 = 42 * 42, fg = new Uint8Array(w * h);
        for (i = 0; i < w * h; i++) {
            if (!inQuad[i]) continue;
            var pr = px[i * 4], pg = px[i * 4 + 1], pb = px[i * 4 + 2], best = Infinity;
            for (var j = 0; j < wall.length && best > T2; j++) {
                var dr = pr - wall[j][0], dg = pg - wall[j][1], db = pb - wall[j][2];
                var d = dr * dr + dg * dg + db * db;
                if (d < best) best = d;
            }
            if (best > T2) fg[i] = 1;
        }

        // 3) Temizlik: kapama (boşlukları doldur) + açma (kırıntıları sil), küçük bölgeleri at, iç delikleri doldur.
        var r1 = Math.max(1, Math.round(w / 320));
        fg = morph(morph(fg, w, h, r1 * 2, true), w, h, r1 * 2, false);
        fg = morph(morph(fg, w, h, r1, false), w, h, r1, true);
        for (i = 0; i < w * h; i++) if (!inQuad[i]) fg[i] = 0;
        var minArea = Math.max(30, n * 0.004), keep = new Uint8Array(w * h);
        components(fg, inQuad, w, h, function (pixels) { if (pixels.length >= minArea) pixels.forEach(function (p) { keep[p] = 1; }); });
        var bg = new Uint8Array(w * h);
        for (i = 0; i < w * h; i++) bg[i] = inQuad[i] && !keep[i] ? 1 : 0;
        components(bg, inQuad, w, h, function (pixels) {
            // Dörtgen kenarına değmeyen küçük duvar adacıkları eşyanın içindeki deliklerdir.
            if (pixels.length > n * 0.08) return;
            for (var t = 0; t < pixels.length; t++) {
                var p = pixels[t], x = p % w, y = (p / w) | 0;
                if (x === 0 || y === 0 || x === w - 1 || y === h - 1 || !inQuad[p - 1] || !inQuad[p + 1] || !inQuad[p - w] || !inQuad[p + w]) return;
            }
            pixels.forEach(function (p) { keep[p] = 1; });
        });
        // Kenarlar tam örtülsün; maske dörtgen sınırının birkaç piksel DIŞINA taşar ki sınırda (eşyanın
        // üstünde) ince poster çizgisi kalmasın - dörtgen dışında maske fotoğrafın kendisi olduğundan zararsızdır.
        keep = morph(keep, w, h, 2, true);
        var quadGrow = morph(inQuad, w, h, 3, true);
        for (i = 0; i < w * h; i++) if (!quadGrow[i]) keep[i] = 0;

        var covered = 0;
        var out = document.createElement('canvas'); out.width = w; out.height = h;
        var octx = out.getContext('2d');
        var od = octx.createImageData(w, h);
        for (i = 0; i < w * h; i++) {
            if (!keep[i]) continue;
            covered++;
            od.data[i * 4] = 233; od.data[i * 4 + 1] = 102; od.data[i * 4 + 2] = 49; od.data[i * 4 + 3] = 255;
        }
        octx.putImageData(od, 0, 0);
        return { canvas: out, coverage: covered / n };
    }

    function buildModal() {
        var el = document.createElement('div');
        el.className = 'modal fade';
        el.tabIndex = -1;
        el.setAttribute('aria-labelledby', 'roomUploadTitle');
        el.innerHTML =
            '<div class="modal-dialog modal-fullscreen"><div class="modal-content">' +
            ' <div class="modal-header"><h2 class="modal-title h5" id="roomUploadTitle">Kendi odanda dene</h2><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Kapat"></button></div>' +
            ' <div class="modal-body">' +
            '  <div class="alert alert-danger py-2" data-room-error hidden></div>' +
            '  <section data-room-step="1">' +
            '   <p>Duvarın tamamen göründüğü, olabildiğince karşıdan çekilmiş bir fotoğraf seçin (JPG, PNG veya WebP, en fazla 10 MB).</p>' +
            '   <input type="file" class="form-control" accept="image/jpeg,image/png,image/webp" data-room-file aria-label="Oda fotoğrafı" />' +
            '   <p class="small text-muted mt-2 mb-0" data-room-quota></p>' +
            '  </section>' +
            '  <section data-room-step="2" hidden>' +
            '   <p class="mb-2">Köşe noktalarını duvarın köşelerine sürükleyin. Klavye: noktaya Tab ile gelip ok tuşlarıyla (Shift ile hızlı) taşıyın.</p>' +
            '   <div class="room-editor" data-room-editor><img data-room-img alt="Yüklenen oda fotoğrafı" /><svg data-room-svg aria-hidden="true"><polygon data-room-poly /></svg></div>' +
            '   <div class="row g-2 mt-2">' +
            '    <div class="col-sm-4"><label class="form-label small mb-0" for="roomWallW">Duvarın gerçek genişliği (cm) *</label><input id="roomWallW" class="form-control form-control-sm" inputmode="decimal" data-room-width required /></div>' +
            '    <div class="col-sm-4"><label class="form-label small mb-0" for="roomWallH">Yüksekliği (cm, isteğe bağlı)</label><input id="roomWallH" class="form-control form-control-sm" inputmode="decimal" data-room-height /></div>' +
            '    <div class="col-sm-4"><label class="form-label small mb-0" for="roomName">Oda adı</label><input id="roomName" class="form-control form-control-sm" maxlength="60" value="Odam" data-room-name /></div>' +
            '   </div>' +
            '  </section>' +
            '  <section data-room-step="3" hidden>' +
            '   <div class="alert alert-info py-2 small mb-2"><strong>Duvarın önünde eşya var mı?</strong> Koltuk, lamba, bitki gibi duvarın <strong>önünde</strong> duran her şeyi işaretleyin; ' +
            'seçtiğiniz ürün bu eşyaların <strong>arkasında</strong> görünür. İşaretlemediğiniz alanlar ürünle kaplanır.</div>' +
            '   <div class="alert alert-secondary py-2 small mb-2 d-flex flex-wrap align-items-center gap-2" data-room-auto-box>' +
            '    <button type="button" class="btn btn-sm btn-primary" data-room-auto><i class="bi bi-magic"></i> Eşyaları otomatik bul</button>' +
            '    <span data-room-auto-status>Duvar rengiyle uyuşmayan eşyalar (koltuk, lamba, bitki) otomatik işaretlenir; sonucu kontrol edip gerekirse fırça/silgiyle düzeltin.</span>' +
            '   </div>' +
            '   <div class="d-flex flex-wrap gap-2 align-items-center mb-2">' +
            '    <div class="btn-group btn-group-sm" role="group" aria-label="Araç">' +
            '     <input type="radio" class="btn-check" name="roomTool" id="roomToolPoly" value="poly" checked><label class="btn btn-outline-secondary" for="roomToolPoly" title="Eşyanın çevresine tıklayarak noktalar koyun, ilk noktaya tıklayınca kapanır"><i class="bi bi-bounding-box-circles"></i> Çevresini çiz</label>' +
            '     <input type="radio" class="btn-check" name="roomTool" id="roomToolPaint" value="paint"><label class="btn btn-outline-secondary" for="roomToolPaint"><i class="bi bi-brush"></i> Fırça</label>' +
            '     <input type="radio" class="btn-check" name="roomTool" id="roomToolErase" value="erase"><label class="btn btn-outline-secondary" for="roomToolErase"><i class="bi bi-eraser"></i> Silgi</label>' +
            '     <input type="radio" class="btn-check" name="roomTool" id="roomToolPan" value="pan"><label class="btn btn-outline-secondary" for="roomToolPan" title="Yakınlaştırılmış fotoğrafı sürükleyerek kaydırın (her araçta Boşluk tuşunu basılı tutarak da kaydırabilirsiniz)"><i class="bi bi-arrows-move"></i> El (kaydır)</label>' +
            '    </div>' +
            '    <div class="btn-group btn-group-sm ms-lg-2" role="group" aria-label="Yakınlaştırma">' +
            '     <button type="button" class="btn btn-outline-secondary" data-room-zoom-out aria-label="Uzaklaştır"><i class="bi bi-zoom-out"></i></button>' +
            '     <span class="btn btn-outline-secondary disabled room-zoom-label" data-room-zoom-label>100%</span>' +
            '     <button type="button" class="btn btn-outline-secondary" data-room-zoom-in aria-label="Yakınlaştır"><i class="bi bi-zoom-in"></i></button>' +
            '     <button type="button" class="btn btn-outline-secondary" data-room-zoom-fit title="Fotoğrafı ekrana sığdır"><i class="bi bi-fullscreen-exit"></i> Sığdır</button>' +
            '    </div>' +
            '    <label class="small" data-room-brush-box hidden>Fırça boyu <input type="range" min="5" max="120" value="40" data-room-brush></label>' +
            '    <button type="button" class="btn btn-sm btn-outline-primary" data-room-poly-close hidden>Şekli kapat</button>' +
            '    <button type="button" class="btn btn-sm btn-link" data-room-poly-cancel hidden>Çizimi iptal</button>' +
            '    <button type="button" class="btn btn-sm btn-link" data-room-undo>Geri al</button>' +
            '    <button type="button" class="btn btn-sm btn-link text-danger" data-room-clear>Tümünü temizle</button>' +
            '   </div>' +
            '   <p class="small text-muted mb-2" data-room-tool-help>Eşyanın kenarları boyunca tıklayarak noktalar koyun; ilk noktaya tıklayınca (veya çift tıklayınca) alan işaretlenir. Birden çok eşya için tekrarlayın.</p>' +
            '   <div class="room-mask-viewport" data-room-viewport title="Fare tekerleğiyle yakınlaştırın; kaydırmak için El aracını seçin ya da Boşluk tuşunu basılı tutup sürükleyin">' +
            '    <div class="room-mask-editor" data-room-mask-editor><img data-room-mask-img alt="" crossorigin="anonymous" draggable="false" /><canvas data-room-mask-canvas></canvas><svg data-room-poly-svg aria-hidden="true"><polyline data-room-poly-line /></svg></div>' +
            '   </div>' +
            '   <p class="small text-muted mt-1 mb-0">Yakınlaştırma: fare tekerleği veya +/− düğmeleri · Kaydırma: "El" aracı, Boşluk + sürükle ya da orta tuşla sürükle · İnce kenarları yakınlaştırıp işaretleyin.</p>' +
            '  </section>' +
            ' </div>' +
            ' <div class="modal-footer">' +
            '  <button type="button" class="btn btn-outline-secondary" data-room-skip hidden>Maskesiz devam et</button>' +
            '  <button type="button" class="btn btn-primary" data-room-next disabled>Devam</button>' +
            ' </div>' +
            '</div></div>';
        document.body.appendChild(el);
        return el;
    }

    function open(options) {
        options = options || {};
        var el = buildModal();
        var modal = new bootstrap.Modal(el);
        var step = 1, file = null, imgUrl = null, natural = { w: 0, h: 0 }, corners = null, scene = null;
        var q = function (sel) { return el.querySelector(sel); };
        var err = function (msg) { var box = q('[data-room-error]'); box.textContent = msg || ''; box.hidden = !msg; };

        fetch('/api/v1/room-previews/quota', { credentials: 'same-origin' }).then(function (r) { return r.json(); }).then(function (d) {
            q('[data-room-quota]').textContent = d.isMember
                ? 'Yüksek kaliteli indirme hakkınız: ' + d.remaining + ' / ' + d.limit + '. Poster değiştirmek hakkınızdan düşmez.'
                : 'Odanızda anında önizleme yapabilirsiniz. Yüksek kaliteli görsel indirmek için giriş yapmanız gerekir.';
        }).catch(function () { });

        q('[data-room-file]').addEventListener('change', function (e) {
            err('');
            file = e.target.files[0] || null;
            q('[data-room-next]').disabled = !file;
            if (!file) return;
            if (file.size > MAX_BYTES) { err('Fotoğraf en fazla 10 MB olabilir.'); file = null; q('[data-room-next]').disabled = true; return; }
            if (!/^image\/(jpeg|png|webp)$/.test(file.type)) { err('Yalnızca JPG, PNG veya WebP seçebilirsiniz.'); file = null; q('[data-room-next]').disabled = true; }
        });

        // ---- Adım 2: köşeler ----
        function showCorners() {
            imgUrl = URL.createObjectURL(file);
            var img = q('[data-room-img]');
            img.onload = function () {
                natural = { w: img.naturalWidth, h: img.naturalHeight };
                corners = [[0.15, 0.12], [0.85, 0.12], [0.85, 0.78], [0.15, 0.78]];
                var editor = q('[data-room-editor]');
                corners.forEach(function (c, i) {
                    var h = document.createElement('button');
                    h.type = 'button';
                    h.className = 'room-handle';
                    h.dataset.index = i;
                    h.setAttribute('aria-label', LABELS[i] + ' köşe');
                    h.textContent = i + 1;
                    editor.appendChild(h);
                    bindHandle(h, editor);
                });
                layoutHandles();
            };
            img.src = imgUrl;
        }

        function layoutHandles() {
            var editor = q('[data-room-editor]');
            var img = q('[data-room-img]');
            var w = img.clientWidth, h = img.clientHeight;
            editor.querySelectorAll('.room-handle').forEach(function (handle) {
                var c = corners[+handle.dataset.index];
                handle.style.left = (c[0] * w) + 'px';
                handle.style.top = (c[1] * h) + 'px';
            });
            var svg = q('[data-room-svg]');
            svg.setAttribute('viewBox', '0 0 ' + w + ' ' + h);
            svg.style.width = w + 'px';
            svg.style.height = h + 'px';
            q('[data-room-poly]').setAttribute('points', corners.map(function (c) { return (c[0] * w) + ',' + (c[1] * h); }).join(' '));
        }

        function bindHandle(handle, editor) {
            var i = +handle.dataset.index;
            handle.addEventListener('pointerdown', function (e) {
                e.preventDefault();
                handle.setPointerCapture(e.pointerId);
                var move = function (ev) {
                    var rect = q('[data-room-img]').getBoundingClientRect();
                    corners[i] = [Math.min(1, Math.max(0, (ev.clientX - rect.left) / rect.width)), Math.min(1, Math.max(0, (ev.clientY - rect.top) / rect.height))];
                    layoutHandles();
                };
                var up = function () { handle.removeEventListener('pointermove', move); handle.removeEventListener('pointerup', up); };
                handle.addEventListener('pointermove', move);
                handle.addEventListener('pointerup', up);
            });
            handle.addEventListener('keydown', function (e) {
                var d = e.shiftKey ? 0.05 : 0.005;
                var delta = { ArrowLeft: [-d, 0], ArrowRight: [d, 0], ArrowUp: [0, -d], ArrowDown: [0, d] }[e.key];
                if (!delta) return;
                e.preventDefault();
                corners[i] = [Math.min(1, Math.max(0, corners[i][0] + delta[0])), Math.min(1, Math.max(0, corners[i][1] + delta[1]))];
                layoutHandles();
            });
        }

        function saveScene() {
            var width = q('[data-room-width]').value.trim();
            if (!width) { err('Duvarın gerçek genişliğini cm olarak girin.'); q('[data-room-width]').focus(); return Promise.resolve(); }
            var fd = new FormData();
            fd.append('photo', file);
            fd.append('corners', corners.map(function (c, idx) { return [c[0] * natural.w, c[1] * natural.h]; }).flat().map(function (v) { return v.toFixed(1); }).join(','));
            fd.append('imageWidth', String(natural.w));
            fd.append('imageHeight', String(natural.h));
            fd.append('wallWidthCm', width);
            fd.append('wallHeightCm', q('[data-room-height]').value.trim());
            fd.append('name', q('[data-room-name]').value.trim());
            return send('/api/v1/room-previews', fd).then(function (s) { scene = s; });
        }

        // ---- Adım 3: ön plan maskesi (duvarın önündeki eşyalar) ----
        // Maske tuvali TAM OPAK boyanır (ekranda CSS saydamlığıyla gösterilir); kaydedilen maskede işaretli
        // alanın alfası 255 olur → ürün eşyanın tamamen arkasında kalır (yarı saydam "hayalet" görünüm olmaz).
        var MASK_COLOR = 'rgb(233,102,49)';
        var maskCtx = null, dirty = false, undoStack = [], poly = [];
        function tool() { return q('input[name="roomTool"]:checked').value; }
        function snapshot() {
            undoStack.push(maskCtx.getImageData(0, 0, natural.w, natural.h));
            if (undoStack.length > 20) undoStack.shift();
        }
        function toImage(e) {
            var rect = q('[data-room-mask-canvas]').getBoundingClientRect();
            return { x: (e.clientX - rect.left) * natural.w / rect.width, y: (e.clientY - rect.top) * natural.h / rect.height, scale: natural.w / rect.width };
        }
        function drawPolyPreview(cursor) {
            var svg = q('[data-room-poly-svg]');
            svg.setAttribute('viewBox', '0 0 ' + natural.w + ' ' + natural.h);
            var pts = poly.concat(cursor ? [cursor] : []);
            var line = q('[data-room-poly-line]');
            line.setAttribute('points', pts.map(function (p) { return p.x + ',' + p.y; }).join(' '));
            line.setAttribute('stroke-width', String(Math.max(2, natural.w / 400)));
            svg.querySelectorAll('circle').forEach(function (c) { c.remove(); });
            poly.forEach(function (p, i) {
                var c = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
                c.setAttribute('cx', p.x); c.setAttribute('cy', p.y);
                c.setAttribute('r', String(Math.max(4, natural.w / (i === 0 ? 120 : 200))));
                c.setAttribute('class', i === 0 ? 'room-poly-start' : 'room-poly-point');
                svg.appendChild(c);
            });
            q('[data-room-poly-close]').hidden = poly.length < 3;
            q('[data-room-poly-cancel]').hidden = poly.length === 0;
        }
        function closePoly() {
            if (poly.length < 3) return;
            snapshot();
            maskCtx.globalCompositeOperation = 'source-over';
            maskCtx.fillStyle = MASK_COLOR;
            maskCtx.beginPath();
            poly.forEach(function (p, i) { if (i === 0) maskCtx.moveTo(p.x, p.y); else maskCtx.lineTo(p.x, p.y); });
            maskCtx.closePath();
            maskCtx.fill();
            poly = [];
            dirty = true;
            drawPolyPreview(null);
        }
        function updateToolUi() {
            var t = tool();
            q('[data-room-brush-box]').hidden = t === 'poly' || t === 'pan';
            q('[data-room-tool-help]').textContent = t === 'poly'
                ? 'Eşyanın kenarları boyunca tıklayarak noktalar koyun; ilk noktaya tıklayınca (veya çift tıklayınca) alan işaretlenir. Birden çok eşya için tekrarlayın.'
                : t === 'paint' ? 'Eşyanın üzerini boyayın. İnce kenarlar için yakınlaştırın veya fırçayı küçültün.'
                : t === 'erase' ? 'Yanlış işaretlenen yerleri silin.'
                : 'Fotoğrafı sürükleyerek kaydırın; fare tekerleğiyle yakınlaştırın.';
            q('[data-room-viewport]').classList.toggle('is-pan-tool', t === 'pan');
            if ((t === 'paint' || t === 'erase') && poly.length) { poly = []; drawPolyPreview(null); }
        }

        // ---- Yakınlaştırma / kaydırma ----
        // Fotoğraf + maske + çizim katmanı tek bir "sahne" öğesinde; ölçek ve kaydırma CSS transform ile uygulanır.
        // toImage() getBoundingClientRect ile çalıştığı için boyama/çizim koordinatları yakınlaştırmada da doğrudur;
        // fırça kalınlığı ekran pikseline göre kalır (yakınlaştırınca fotoğrafta daha ince iz → hassas kenar).
        var view = { s: 1, x: 0, y: 0, baseW: 0, baseH: 0 }, MAX_ZOOM = 8, spaceDown = false, panDrag = null;
        function viewportEl() { return q('[data-room-viewport]'); }
        function applyView() {
            var editor = q('[data-room-mask-editor]');
            editor.style.width = view.baseW + 'px';
            editor.style.height = view.baseH + 'px';
            editor.style.transform = 'translate(' + view.x + 'px,' + view.y + 'px) scale(' + view.s + ')';
            q('[data-room-zoom-label]').textContent = Math.round(view.s * 100) + '%';
        }
        function clampView() {
            var vp = viewportEl(), w = view.baseW * view.s, h = view.baseH * view.s;
            var vw = vp.clientWidth, vh = vp.clientHeight;
            // Sahne görüş alanından küçükse ortalanır; büyükse kenarlarda boşluk kalmayacak şekilde sınırlanır.
            view.x = w <= vw ? (vw - w) / 2 : Math.min(0, Math.max(vw - w, view.x));
            view.y = h <= vh ? (vh - h) / 2 : Math.min(0, Math.max(vh - h, view.y));
        }
        function fitView() {
            var vp = viewportEl();
            if (!vp.clientWidth || !natural.w) return;
            var sc = Math.min(vp.clientWidth / natural.w, vp.clientHeight / natural.h);
            view.baseW = natural.w * sc;
            view.baseH = natural.h * sc;
            view.s = 1;
            clampView();
            applyView();
        }
        function zoomAt(factor, cx, cy) {
            var ns = Math.min(MAX_ZOOM, Math.max(1, view.s * factor));
            var k = ns / view.s;
            view.x = cx - (cx - view.x) * k;
            view.y = cy - (cy - view.y) * k;
            view.s = ns;
            clampView();
            applyView();
        }
        function zoomCenter(factor) { var vp = viewportEl(); zoomAt(factor, vp.clientWidth / 2, vp.clientHeight / 2); }
        function wantsPan(e) { return tool() === 'pan' || spaceDown || e.button === 1; }
        function bindZoom() {
            var vp = viewportEl();
            vp.addEventListener('wheel', function (e) {
                e.preventDefault();
                var r = vp.getBoundingClientRect();
                zoomAt(e.deltaY < 0 ? 1.15 : 1 / 1.15, e.clientX - r.left, e.clientY - r.top);
            }, { passive: false });
            vp.addEventListener('pointerdown', function (e) {
                if (!wantsPan(e)) return;
                e.preventDefault();
                panDrag = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y };
                vp.setPointerCapture(e.pointerId);
                vp.classList.add('is-panning');
            });
            vp.addEventListener('pointermove', function (e) {
                if (!panDrag) return;
                view.x = panDrag.vx + e.clientX - panDrag.x;
                view.y = panDrag.vy + e.clientY - panDrag.y;
                clampView();
                applyView();
            });
            var endPan = function () { panDrag = null; vp.classList.remove('is-panning'); };
            vp.addEventListener('pointerup', endPan);
            vp.addEventListener('pointercancel', endPan);
            vp.addEventListener('auxclick', function (e) { if (e.button === 1) e.preventDefault(); });
            q('[data-room-zoom-in]').addEventListener('click', function () { zoomCenter(1.4); });
            q('[data-room-zoom-out]').addEventListener('click', function () { zoomCenter(1 / 1.4); });
            q('[data-room-zoom-fit]').addEventListener('click', fitView);
            // Boşluk tuşu basılıyken her araçta geçici kaydırma (metin kutularında devre dışı).
            var keydown = function (e) {
                if (e.code !== 'Space' || step !== 3 || e.target.closest('input, textarea, select, button')) return;
                e.preventDefault();
                if (!spaceDown) { spaceDown = true; vp.classList.add('is-space-pan'); }
            };
            var keyup = function (e) { if (e.code === 'Space') { spaceDown = false; vp.classList.remove('is-space-pan'); } };
            document.addEventListener('keydown', keydown);
            document.addEventListener('keyup', keyup);
            window.addEventListener('resize', function () { if (step === 3) fitView(); });
            el.addEventListener('shown.bs.modal', function () { if (step === 3) fitView(); });
            el.addEventListener('hidden.bs.modal', function () {
                document.removeEventListener('keydown', keydown);
                document.removeEventListener('keyup', keyup);
            });
        }

        function quadPoints() {
            if (options.editScene) {
                var wq = options.editScene.wallQuad;
                return [[wq[0], wq[1]], [wq[2], wq[3]], [wq[4], wq[5]], [wq[6], wq[7]]];
            }
            return corners.map(function (c) { return [c[0] * natural.w, c[1] * natural.h]; });
        }
        function runAuto() {
            var status = q('[data-room-auto-status]');
            var res = null;
            try { res = detectForeground(q('[data-room-mask-img]'), quadPoints(), natural.w, natural.h); } catch (e) { res = null; }
            if (!res || res.coverage === 0) {
                status.textContent = 'Duvarın önünde eşya bulunamadı. Varsa "Çevresini çiz" ile işaretleyin.';
                return;
            }
            if (res.coverage > 0.55) {
                status.textContent = 'Duvar desenli veya çok renkli olduğu için otomatik bulma güvenilir değil; eşyaları "Çevresini çiz" ile işaretleyin.';
                return;
            }
            snapshot();
            maskCtx.globalCompositeOperation = 'source-over';
            maskCtx.imageSmoothingEnabled = true;
            maskCtx.drawImage(res.canvas, 0, 0, natural.w, natural.h);
            dirty = true;
            status.textContent = 'Eşyalar otomatik işaretlendi (turuncu alanlar). Lütfen kontrol edin; eksik ya da fazla yerleri fırça/silgi ile düzeltin.';
        }

        function showMask(existingMaskUrl) {
            q('[data-room-auto]').addEventListener('click', function () { if (maskCtx) runAuto(); });
            bindZoom();
            var img = q('[data-room-mask-img]');
            var canvas = q('[data-room-mask-canvas]');
            img.onload = function () {
                canvas.width = natural.w;
                canvas.height = natural.h;
                fitView();
                maskCtx = canvas.getContext('2d', { willReadFrequently: true });
                // Yeni yüklemede eşyalar hemen otomatik bulunur; müşteri yalnızca kontrol edip düzeltir.
                if (!existingMaskUrl) { runAuto(); return; }
                // Düzenleme: mevcut maskenin işaretli (alfa > 0) alanları turuncu olarak yüklenir.
                var m = new Image();
                m.onload = function () {
                    maskCtx.drawImage(m, 0, 0, natural.w, natural.h);
                    maskCtx.globalCompositeOperation = 'source-in';
                    maskCtx.fillStyle = MASK_COLOR;
                    maskCtx.fillRect(0, 0, natural.w, natural.h);
                    maskCtx.globalCompositeOperation = 'source-over';
                };
                m.src = existingMaskUrl;
            };
            img.onerror = function () { err('Oda fotoğrafı yüklenemedi.'); };
            img.src = imgUrl;

            var drawing = false, last = null;
            function stroke(from, to) {
                maskCtx.globalCompositeOperation = tool() === 'erase' ? 'destination-out' : 'source-over';
                maskCtx.strokeStyle = MASK_COLOR;
                maskCtx.lineCap = 'round';
                maskCtx.lineJoin = 'round';
                maskCtx.lineWidth = +q('[data-room-brush]').value * to.scale;
                // Hızlı fırça darbelerinde boşluk kalmasın: iki olay arası çizgiyle birleştirilir.
                maskCtx.beginPath(); maskCtx.moveTo(from.x, from.y); maskCtx.lineTo(to.x + 0.01, to.y); maskCtx.stroke();
                dirty = true;
            }
            canvas.addEventListener('pointerdown', function (e) {
                if (!maskCtx || wantsPan(e)) return; // kaydırmayı görüş alanı (viewport) yönetir
                e.preventDefault();
                var p = toImage(e);
                if (tool() === 'poly') {
                    // İlk noktaya yakın tıklama şekli kapatır.
                    if (poly.length >= 3 && Math.hypot(p.x - poly[0].x, p.y - poly[0].y) < 14 * p.scale) { closePoly(); return; }
                    poly.push({ x: p.x, y: p.y });
                    drawPolyPreview(null);
                    return;
                }
                snapshot();
                drawing = true;
                last = p;
                canvas.setPointerCapture(e.pointerId);
                stroke(p, p);
            });
            canvas.addEventListener('pointermove', function (e) {
                if (!maskCtx || panDrag) return;
                var p = toImage(e);
                if (tool() === 'poly') { if (poly.length) drawPolyPreview(p); return; }
                if (!drawing) return;
                stroke(last, p);
                last = p;
            });
            canvas.addEventListener('pointerup', function () { drawing = false; last = null; });
            canvas.addEventListener('dblclick', function (e) {
                if (tool() !== 'poly') return;
                e.preventDefault();
                closePoly();
            });
            el.querySelectorAll('input[name="roomTool"]').forEach(function (r) { r.addEventListener('change', updateToolUi); });
            q('[data-room-poly-close]').addEventListener('click', closePoly);
            q('[data-room-poly-cancel]').addEventListener('click', function () { poly = []; drawPolyPreview(null); });
            q('[data-room-undo]').addEventListener('click', function () {
                if (poly.length) { poly.pop(); drawPolyPreview(null); return; }
                var prev = undoStack.pop();
                if (prev) { maskCtx.putImageData(prev, 0, 0); dirty = true; }
            });
            q('[data-room-clear]').addEventListener('click', function () {
                snapshot();
                maskCtx.clearRect(0, 0, natural.w, natural.h);
                poly = [];
                drawPolyPreview(null);
                dirty = true;
            });
            updateToolUi();
        }

        function saveMask() {
            if (poly.length >= 3) closePoly(); // kapatılmamış şekil unutulmasın
            if (!dirty) return Promise.resolve();
            // Ön plan katmanı = fotoğrafın kendi pikselleri, alfa = işaretli mi (tam opak). Hiç işaret yoksa
            // tamamen saydam PNG gider (düzenlemede "tümünü temizle" maskeyi kaldırır).
            var out = document.createElement('canvas');
            out.width = natural.w;
            out.height = natural.h;
            var ctx = out.getContext('2d');
            ctx.drawImage(q('[data-room-mask-img]'), 0, 0, natural.w, natural.h);
            ctx.globalCompositeOperation = 'destination-in';
            ctx.drawImage(q('[data-room-mask-canvas]'), 0, 0);
            return new Promise(function (resolve) { out.toBlob(resolve, 'image/png'); }).then(function (blob) {
                var fd = new FormData();
                fd.append('mask', blob, 'mask.png');
                return send('/api/v1/room-previews/' + scene.id + '/mask', fd).then(function (s) { scene = s; });
            });
        }

        function showStep(n) {
            step = n;
            el.querySelectorAll('[data-room-step]').forEach(function (s) { s.hidden = +s.dataset.roomStep !== n; });
            var next = q('[data-room-next]');
            next.textContent = n === 1 ? 'Devam' : n === 2 ? 'Odayı kaydet' : 'İşaretlemeyi kaydet ve bitir';
            if (n === 3) next.disabled = false;
            var skip = q('[data-room-skip]');
            skip.hidden = n !== 3 || !!options.editScene;
            skip.textContent = 'Duvarın önünde eşya yok, devam et';
        }

        function finish() {
            modal.hide();
            if (options.editScene) { if (options.onMaskSaved) options.onMaskSaved(scene); }
            else if (options.onCreated) options.onCreated(scene);
        }

        q('[data-room-next]').addEventListener('click', function () {
            var btn = q('[data-room-next]');
            err('');
            if (step === 1) { showStep(2); showCorners(); return; }
            btn.disabled = true;
            (step === 2 ? saveScene().then(function () { if (scene) { showStep(3); showMask(null); } }) : saveMask().then(finish))
                .catch(function (e) { err(e.message); })
                .finally(function () { btn.disabled = false; });
        });
        q('[data-room-skip]').addEventListener('click', finish);
        window.addEventListener('resize', function () { if (step === 2 && corners) layoutHandles(); });
        el.addEventListener('hidden.bs.modal', function () { if (imgUrl && imgUrl.indexOf('blob:') === 0) URL.revokeObjectURL(imgUrl); el.remove(); });

        if (options.editScene) {
            // Var olan kendi odası: doğrudan eşya işaretleme adımı, mevcut maske yüklenmiş olarak.
            var existing = options.editScene;
            scene = existing;
            natural = { w: existing.imageWidthPx, h: existing.imageHeightPx };
            imgUrl = existing.baseImageUrl;
            q('#roomUploadTitle').textContent = 'Duvarın önündeki eşyaları işaretle';
            showStep(3);
            showMask(existing.foregroundMaskUrl);
        }

        modal.show();
    }

    window.DekorrasRoomUpload = { open: open };
})();
