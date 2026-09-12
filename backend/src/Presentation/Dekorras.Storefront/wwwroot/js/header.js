// Sticky header küçülme davranışı + mega-menü dokunmatik/klavye toggle'ı.
// Masaüstünde mega-menü CSS :hover ile açılır (bkz. mega-menu.css) - bu dosya yalnızca
// scroll durumunu ve dokunmatik cihazlardaki tap-to-toggle davranışını yönetir.
(function () {
    var header = document.querySelector('.dk-header');
    if (header) {
        var threshold = 80;
        var ticking = false;
        var updateHeader = function () {
            header.classList.toggle('dk-header--compact', window.scrollY > threshold);
            ticking = false;
        };
        window.addEventListener('scroll', function () {
            if (ticking) return;
            ticking = true;
            window.requestAnimationFrame(updateHeader);
        }, { passive: true });
        updateHeader();
    }

    var canHover = window.matchMedia('(hover: hover)').matches;
    if (!canHover) {
        document.querySelectorAll('.dk-mega-item > .dk-mega-link').forEach(function (link) {
            link.addEventListener('click', function (e) {
                var item = link.closest('.dk-mega-item');
                if (!item.querySelector('.dk-mega-panel')) return;
                if (!item.classList.contains('is-open')) {
                    e.preventDefault();
                    document.querySelectorAll('.dk-mega-item.is-open').forEach(function (open) {
                        if (open !== item) open.classList.remove('is-open');
                    });
                    item.classList.add('is-open');
                }
            });
        });

        document.addEventListener('click', function (e) {
            if (!e.target.closest('.dk-mega-item')) {
                document.querySelectorAll('.dk-mega-item.is-open').forEach(function (open) {
                    open.classList.remove('is-open');
                });
            }
        });
    }

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            document.querySelectorAll('.dk-mega-item.is-open').forEach(function (open) {
                open.classList.remove('is-open');
            });
        }
    });
})();
