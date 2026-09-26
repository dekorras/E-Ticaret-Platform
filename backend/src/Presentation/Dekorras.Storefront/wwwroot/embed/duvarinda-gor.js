/*!
 * Dekorras "Duvarında Gör" gömülebilir widget (spec 1.6.6-D)
 *
 * Kullanım:
 *   <script src="https://{alanadi}/embed/duvarinda-gor.js" data-api-key="PUBLIC_KEY" defer></script>
 *   <a data-duvarinda-gor data-product="klasik-orman-manzarali">Duvarında Gör</a>
 *   <a data-duvarinda-gor data-image="https://cdn.magaza.com/poster.jpg" data-width-cm="300" data-height-cm="250" data-type="mural">Duvarında Gör</a>
 *
 * Düğmeyi script'e ekletmek için: data-auto-inject=".urun-gorseli" (hedefin data-product/data-image değerleri
 * veya script'teki data-product/data-image kullanılır).
 *
 * Ana site entegrasyonu (kendi sepetinize eklemek için):
 *   document.addEventListener('duvarindagor:add-to-cart', function (e) { e.detail.product; e.detail.configuration; });
 *   document.addEventListener('duvarindagor:apply', function (e) { ... });
 */
(function () {
    'use strict';
    var script = document.currentScript || document.querySelector('script[src*="/embed/duvarinda-gor.js"]');
    if (!script || window.DuvarindaGor) return;

    var base = new URL(script.src, location.href).origin;
    var key = script.getAttribute('data-api-key') || '';
    var label = script.getAttribute('data-label') || 'Duvarında Gör';
    var overlay = null, frame = null, activeElement = null, lastFocus = null;

    function css(el, styles) { for (var k in styles) el.style[k] = styles[k]; }

    function buildUrl(data) {
        var q = new URLSearchParams();
        q.set('key', key);
        q.set('origin', location.origin);
        if (data.product) q.set('product', data.product);
        if (data.image) q.set('image', data.image);
        if (data.widthCm) q.set('w_cm', data.widthCm);
        if (data.heightCm) q.set('h_cm', data.heightCm);
        if (data.type) q.set('type', data.type);
        if (data.material) q.set('material', data.material);
        return base + '/embed/duvarinda-gor?' + q.toString();
    }

    function dataOf(el) {
        return {
            product: el.getAttribute('data-product'),
            image: el.getAttribute('data-image'),
            widthCm: el.getAttribute('data-width-cm'),
            heightCm: el.getAttribute('data-height-cm'),
            type: el.getAttribute('data-type'),
            material: el.getAttribute('data-material')
        };
    }

    function open(data, element) {
        if (!key) { console.warn('[Duvarında Gör] data-api-key eksik.'); return; }
        activeElement = element || null;
        lastFocus = document.activeElement;
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.setAttribute('role', 'dialog');
            overlay.setAttribute('aria-modal', 'true');
            overlay.setAttribute('aria-label', label);
            css(overlay, { position: 'fixed', inset: '0', zIndex: '2147483000', background: 'rgba(0,0,0,.55)', display: 'none' });
            frame = document.createElement('iframe');
            frame.title = label;
            frame.setAttribute('allow', 'fullscreen');
            css(frame, { position: 'absolute', inset: '0', width: '100%', height: '100%', border: '0', background: '#fff' });
            overlay.appendChild(frame);
            document.body.appendChild(overlay);
        }
        frame.src = buildUrl(data);
        overlay.style.display = 'block';
        document.documentElement.style.overflow = 'hidden';
        frame.focus();
    }

    function close() {
        if (!overlay) return;
        overlay.style.display = 'none';
        frame.src = 'about:blank';
        document.documentElement.style.overflow = '';
        if (lastFocus && lastFocus.focus) lastFocus.focus();
    }

    function emit(name, detail) {
        var event = new CustomEvent(name, { detail: detail, bubbles: true });
        (activeElement || document).dispatchEvent(event);
    }

    window.addEventListener('message', function (e) {
        // Yalnızca kendi iframe'imizden ve kendi origin'imizden gelen mesajlar.
        if (e.origin !== base || !frame || e.source !== frame.contentWindow || !e.data || e.data.source !== 'dekorras-wall') return;
        switch (e.data.type) {
            case 'wallpreview:close': close(); break;
            case 'wallpreview:apply':
                emit('duvarindagor:apply', { product: e.data.configuration && e.data.configuration.product, configuration: e.data.configuration });
                close();
                break;
            case 'wallpreview:add-to-cart':
                emit('duvarindagor:add-to-cart', { product: e.data.product, configuration: e.data.configuration });
                break;
            case 'wallpreview:ready':
            case 'wallpreview:resize':
                break;
        }
    });

    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && overlay && overlay.style.display === 'block') close(); });

    document.addEventListener('click', function (e) {
        var el = e.target.closest && e.target.closest('[data-duvarinda-gor]');
        if (!el) return;
        e.preventDefault();
        open(dataOf(el), el);
    });

    // Harici görseli ilk hover'da sunucuya kaydettir (açılışı hızlandırır).
    var warmed = {};
    document.addEventListener('pointerover', function (e) {
        var el = e.target.closest && e.target.closest('[data-duvarinda-gor][data-image]');
        if (!el) return;
        var image = el.getAttribute('data-image');
        if (!image || warmed[image]) return;
        warmed[image] = true;
        fetch(base + '/api/v1/embed/external-images', {
            method: 'POST', mode: 'cors', credentials: 'omit',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key: key, imageUrl: image })
        }).catch(function () { warmed[image] = false; });
    }, { passive: true });

    function autoInject() {
        var selector = script.getAttribute('data-auto-inject');
        if (!selector) return;
        document.querySelectorAll(selector).forEach(function (target) {
            if (target.getAttribute('data-duvarinda-gor-injected')) return;
            target.setAttribute('data-duvarinda-gor-injected', '1');
            var button = document.createElement('button');
            button.type = 'button';
            button.textContent = label;
            button.setAttribute('data-duvarinda-gor', '');
            ['product', 'image', 'width-cm', 'height-cm', 'type', 'material'].forEach(function (name) {
                var value = target.getAttribute('data-' + name) || script.getAttribute('data-' + name);
                if (value) button.setAttribute('data-' + name, value);
            });
            css(button, { margin: '8px 0', padding: '8px 14px', border: '1px solid #222', borderRadius: '4px', background: '#fff', cursor: 'pointer', font: 'inherit' });
            target.insertAdjacentElement('afterend', button);
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', autoInject); else autoInject();

    window.DuvarindaGor = { open: function (data) { open(data || {}, null); }, close: close };
})();
