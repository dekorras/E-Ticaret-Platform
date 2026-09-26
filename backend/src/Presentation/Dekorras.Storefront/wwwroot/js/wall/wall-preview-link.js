// "Duvarında Gör" bağlantıları (spec 1.6.6-C, progressive enhancement). Bağlantılar gerçek <a href>'dir:
// JS yoksa tam sayfa /duvarinda-gor açılır. JS varsa tıklama yakalanır, görüntüleyici tam ekran bir
// <dialog> içinde (iframe, ?modal=1) açılır ve URL history.pushState ile güncellenir; tarayıcının geri
// tuşu modalı kapatır. Mobilde modal yerine tam sayfa. İlk hover/focus'ta sayfa önceden yüklenir.
(function () {
    'use strict';
    if (document.body.classList.contains('wall-bare')) return; // görüntüleyicinin kendi iframe'i

    var dialog = null, frame = null, openedFrom = null;

    function isMobile() { return window.matchMedia('(max-width: 767.98px)').matches; }

    function track(link) {
        try {
            var body = JSON.stringify({ type: 'wall_preview_link_click', source: link.dataset.wallPreviewSource || null, product: link.dataset.wallPreview || null });
            if (navigator.sendBeacon) navigator.sendBeacon('/api/v1/events/wall-preview', new Blob([body], { type: 'application/json' }));
        } catch (e) { /* yok say */ }
    }

    function prefetch(link) {
        if (link.dataset.wallPrefetched) return;
        link.dataset.wallPrefetched = '1';
        var l = document.createElement('link');
        l.rel = 'prefetch';
        l.href = link.href;
        document.head.appendChild(l);
    }
    ['pointerover', 'focusin'].forEach(function (type) {
        document.addEventListener(type, function (e) {
            var link = e.target.closest && e.target.closest('a[data-wall-preview]');
            if (link) prefetch(link);
        }, { passive: true });
    });

    function ensureDialog() {
        if (dialog) return;
        dialog = document.createElement('dialog');
        dialog.className = 'wall-dialog';
        dialog.setAttribute('aria-label', 'Duvarında Gör');
        frame = document.createElement('iframe');
        frame.title = 'Duvarında Gör';
        frame.setAttribute('allow', 'fullscreen');
        dialog.appendChild(frame);
        document.body.appendChild(dialog);
        dialog.addEventListener('cancel', function (e) { e.preventDefault(); close(); });
    }

    function open(href) {
        ensureDialog();
        var url = new URL(href, location.origin);
        url.searchParams.set('modal', '1');
        frame.src = url.pathname + url.search;
        openedFrom = location.href;
        history.pushState({ wallPreviewModal: true }, '', href);
        dialog.showModal();
        document.documentElement.classList.add('wall-dialog-open');
        frame.focus();
    }

    function close(fromPopState) {
        if (!dialog || !dialog.open) return;
        dialog.close();
        frame.src = 'about:blank';
        document.documentElement.classList.remove('wall-dialog-open');
        if (!fromPopState && history.state && history.state.wallPreviewModal) history.back();
        else if (!fromPopState && openedFrom) history.replaceState(null, '', openedFrom);
    }

    document.addEventListener('click', function (e) {
        var link = e.target.closest('a[data-wall-preview]');
        if (!link || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        track(link);
        if (isMobile() || typeof HTMLDialogElement !== 'function') return; // tam sayfa
        e.preventDefault();
        open(link.href);
    });

    window.addEventListener('popstate', function () { if (dialog && dialog.open) close(true); });

    window.addEventListener('message', function (e) {
        if (e.origin !== location.origin || !e.data || e.data.source !== 'dekorras-wall') return;
        switch (e.data.type) {
            case 'wallpreview:close':
                close();
                break;
            case 'wallpreview:apply': {
                var cfg = e.data.configuration || {};
                if (window.DekorrasConfigurator && window.DekorrasConfigurator.slug === cfg.product) {
                    // Aynı sayfa: konfigüratöre olayla geri aktarılır.
                    document.dispatchEvent(new CustomEvent('wallpreview:apply', { detail: cfg }));
                    close();
                } else {
                    // Farklı sayfa (katalog/sepet): ürün sayfasına konfigürasyonla gidilir.
                    var q = new URLSearchParams({ slug: cfg.product, view: 'customizer' });
                    ['material', 'unit', 'w_cm', 'h_cm', 'fit', 'mirror', 'filter', 'crop'].forEach(function (k) { if (cfg[k] != null) q.set(k, cfg[k]); });
                    location.href = '/Product/Details?' + q.toString();
                }
                break;
            }
            case 'wallpreview:add-to-cart':
                if (window.DekorrasWall && window.DekorrasWall.load) window.DekorrasWall.load();
                break;
        }
    });
})();
