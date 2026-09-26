// "Tasarım değişikliği iste" (spec 1.8): ad, e-posta, talep türü, mesaj ve en fazla 5 görsel (JPG/PNG/WebP,
// en fazla 10 MB). Ürün ve o anki konfigürasyon talebe otomatik eklenir. Sunucu dosyaları içerikten doğrular.
(function () {
    'use strict';

    var TYPES = [['OzelOlcu', 'Özel ölçü'], ['RenkDegisikligi', 'Renk değişikliği'], ['GorselDuzenlemeKirpma', 'Görsel düzenleme / kırpma'], ['OgeEkleKaldir', 'Öğe ekle / kaldır'], ['Diger', 'Diğer']];
    var UNIT = { cm: 0, m: 1, 'in': 2, ft: 3 };
    var FILTER = { none: 0, grayscale: 1, sepia: 2 };

    function token() { var i = document.querySelector('input[name="__RequestVerificationToken"]'); return i ? i.value : ''; }

    /** Konfigüratörün URL biçimli yükünü sunucudaki WallConfiguration JSON biçimine çevirir. */
    function toConfigurationJson(c) {
        if (!c) return '';
        var crop = c.crop ? c.crop.split(',').map(parseFloat) : null;
        return JSON.stringify({
            widthCm: parseFloat(c.w_cm), heightCm: parseFloat(c.h_cm), unit: UNIT[c.unit] || 0, materialCode: c.material,
            fit: c.fit === 'stretch' ? 1 : 0, mirror: c.mirror === '1', filter: FILTER[c.filter] || 0,
            crop: crop && crop.length === 4 ? { x: crop[0], y: crop[1], w: crop[2], h: crop[3] } : null
        });
    }

    function open(button) {
        var el = document.createElement('div');
        el.className = 'modal fade';
        el.tabIndex = -1;
        el.setAttribute('aria-labelledby', 'designReqTitle');
        el.innerHTML =
            '<div class="modal-dialog"><form class="modal-content" novalidate>' +
            '<div class="modal-header"><h2 class="modal-title h5" id="designReqTitle">Tasarım değişikliği iste</h2><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Kapat"></button></div>' +
            '<div class="modal-body">' +
            ' <div class="alert alert-danger py-2" data-dr-error hidden></div>' +
            ' <div class="mb-2"><label class="form-label small mb-0" for="drName">Ad soyad *</label><input id="drName" name="fullName" class="form-control" required maxlength="200" autocomplete="name"></div>' +
            ' <div class="mb-2"><label class="form-label small mb-0" for="drEmail">E-posta *</label><input id="drEmail" name="email" type="email" class="form-control" required autocomplete="email"></div>' +
            ' <div class="mb-2"><label class="form-label small mb-0" for="drType">Talep türü *</label><select id="drType" name="requestType" class="form-select">' +
            TYPES.map(function (t) { return '<option value="' + t[0] + '">' + t[1] + '</option>'; }).join('') + '</select></div>' +
            ' <div class="mb-2"><label class="form-label small mb-0" for="drMessage">Talebiniz *</label><textarea id="drMessage" name="message" class="form-control" rows="4" required maxlength="4000"></textarea></div>' +
            ' <div class="mb-1"><label class="form-label small mb-0" for="drFiles">Görsel ekle (en fazla 5; JPG, PNG, WebP; her biri en fazla 10 MB)</label><input id="drFiles" name="files" type="file" class="form-control" multiple accept="image/jpeg,image/png,image/webp"></div>' +
            ' <p class="small text-muted mb-0">Seçtiğiniz ürün ve ölçüler talebe otomatik eklenir. 2 iş günü içinde dönüş yapıyoruz.</p>' +
            '</div>' +
            '<div class="modal-footer"><button type="submit" class="btn btn-primary">Gönder</button></div>' +
            '</form></div>';
        document.body.appendChild(el);
        var modal = new bootstrap.Modal(el);
        var form = el.querySelector('form');
        var errorBox = el.querySelector('[data-dr-error]');

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            errorBox.hidden = true;
            if (!form.reportValidity()) return;
            var files = form.querySelector('#drFiles').files;
            if (files.length > 5) { errorBox.textContent = 'En fazla 5 dosya ekleyebilirsiniz.'; errorBox.hidden = false; return; }
            for (var i = 0; i < files.length; i++) {
                if (files[i].size > 10 * 1024 * 1024) { errorBox.textContent = files[i].name + ' 10 MB\'dan büyük.'; errorBox.hidden = false; return; }
            }
            var fd = new FormData(form);
            var cfg = window.DekorrasConfigurator;
            if (cfg) { fd.append('product', cfg.slug); fd.append('configuration', toConfigurationJson(cfg.getConfiguration())); }
            else if (button.dataset.product) fd.append('product', button.dataset.product);
            var submit = form.querySelector('[type=submit]');
            submit.disabled = true;
            fetch('/api/v1/design-requests', { method: 'POST', body: fd, credentials: 'same-origin', headers: { 'RequestVerificationToken': token(), 'Accept': 'application/json' } })
                .then(function (r) {
                    return r.json().catch(function () { return {}; }).then(function (d) {
                        if (!r.ok) {
                            var msg = d.detail || d.title || 'Gönderilemedi.';
                            if (d.errors) msg = Object.keys(d.errors).map(function (k) { return [].concat(d.errors[k]).join(' '); }).join(' ');
                            throw new Error(msg);
                        }
                        el.querySelector('.modal-body').innerHTML = '<div class="alert alert-success mb-0" role="status">Talebiniz alındı. E-posta adresinize bir onay gönderdik; en geç 2 iş günü içinde dönüş yapacağız.</div>';
                        form.querySelector('.modal-footer').remove();
                    });
                })
                .catch(function (err) { errorBox.textContent = err.message; errorBox.hidden = false; })
                .finally(function () { submit.disabled = false; });
        });
        el.addEventListener('hidden.bs.modal', function () { el.remove(); });
        modal.show();
    }

    document.addEventListener('click', function (e) {
        var button = e.target.closest('[data-wall-design-request]');
        if (!button) return;
        e.preventDefault();
        open(button);
    });
})();
