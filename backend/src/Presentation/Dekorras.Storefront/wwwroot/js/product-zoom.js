// Ürün galerisi: thumbnail tıklaması ana görseli değiştirir; masaüstünde (hover+fare) imleç üstünde
// büyüteç etkisi; dokunmatikte ana görsele dokunma zaten-vendored Bootstrap modal'ıyla tam ekran açar.
(function () {
    var gallery = document.querySelector('[data-dk-gallery]');
    if (!gallery) return;

    var main = gallery.querySelector('[data-dk-gallery-main]');
    var mainImg = document.getElementById('dkMainImage');
    var thumbs = gallery.querySelectorAll('.dk-gallery-thumb');
    var canHoverZoom = window.matchMedia('(hover: hover) and (pointer: fine)').matches;

    thumbs.forEach(function (thumb) {
        thumb.addEventListener('click', function () {
            var full = thumb.dataset.full;
            if (!full || !mainImg) return;
            mainImg.src = full;
            thumbs.forEach(function (t) { t.classList.remove('is-active'); });
            thumb.classList.add('is-active');
        });
    });

    if (!main || !mainImg) return;

    if (canHoverZoom) {
        main.addEventListener('mouseenter', function () {
            main.classList.add('is-zoomed');
        });
        main.addEventListener('mousemove', function (e) {
            var rect = main.getBoundingClientRect();
            var x = ((e.clientX - rect.left) / rect.width) * 100;
            var y = ((e.clientY - rect.top) / rect.height) * 100;
            mainImg.style.transformOrigin = x + '% ' + y + '%';
        });
        main.addEventListener('mouseleave', function () {
            main.classList.remove('is-zoomed');
            mainImg.style.transformOrigin = '0 0';
        });
    } else {
        main.addEventListener('click', function () {
            var modalEl = document.getElementById('dkImageZoomModal');
            if (!modalEl || typeof bootstrap === 'undefined') return;
            var modalImg = document.getElementById('dkImageZoomModalImg');
            modalImg.src = mainImg.src;
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
        });
    }
})();
