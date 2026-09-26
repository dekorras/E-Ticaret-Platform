// Poster kataloğu (spec 1.6.1): filtre değişince /duvar-kagitlari/_liste parçası fetch edilir ve
// sonuçlar tam sayfa yenilemeden değişir; tüm durum URL'de (history.pushState) - geri tuşu önceki
// filtreye döner. Sonsuz kaydırma sonraki sayfaları ekler; ?page= bağlantıları JS'siz/SEO için kalır.
(function () {
    'use strict';

    var form = document.querySelector('[data-wall-filters]');
    var results = document.querySelector('[data-wall-results]');
    if (!form || !results) return;

    var sentinel = document.querySelector('[data-wall-sentinel]');
    var loading = document.querySelector('[data-wall-loading]');
    var pagination = document.querySelector('[data-wall-pagination]');
    var totalLabel = document.querySelector('[data-wall-total]');
    var colorPicker = form.querySelector('[data-wall-color]');
    var colorValue = form.querySelector('[data-wall-color-value]');
    var colorClear = form.querySelector('[data-wall-color-clear]');
    var busy = false;
    var requestSeq = 0;

    function currentParams(page) {
        var params = new URLSearchParams();
        new FormData(form).forEach(function (value, key) {
            if (value !== '' && value != null) params.append(key, value);
        });
        if (page && page > 1) params.set('page', String(page));
        return params;
    }

    function grid() { return results.querySelector('[data-wall-grid]'); }

    function refresh(push) {
        var params = currentParams(1);
        var seq = ++requestSeq;
        results.setAttribute('aria-busy', 'true');
        return fetch('/duvar-kagitlari/_liste?' + params.toString(), { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('liste yüklenemedi');
                if (totalLabel) totalLabel.textContent = r.headers.get('X-Total-Count') || totalLabel.textContent;
                return r.text();
            })
            .then(function (html) {
                if (seq !== requestSeq) return; // daha yeni bir istek var
                results.innerHTML = html;
                if (pagination) pagination.hidden = true;
                var url = '/duvar-kagitlari' + (params.toString() ? '?' + params.toString() : '');
                if (push) history.pushState({ wallCatalog: true }, '', url); else history.replaceState({ wallCatalog: true }, '', url);
                if (window.DekorrasWall) window.DekorrasWall.markCards(results);
                observe();
            })
            .catch(function () { form.submit(); }) // fetch başarısızsa klasik GET'e düş
            .finally(function () { results.removeAttribute('aria-busy'); });
    }

    function loadMore() {
        var g = grid();
        if (!g || busy) return;
        var page = parseInt(g.dataset.page, 10);
        var total = parseInt(g.dataset.totalPages, 10);
        if (!(page < total)) return;
        busy = true;
        if (loading) loading.classList.remove('d-none');
        fetch('/duvar-kagitlari/_liste?' + currentParams(page + 1).toString(), { credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error(); return r.text(); })
            .then(function (html) {
                var tmp = document.createElement('div');
                tmp.innerHTML = html;
                var more = tmp.querySelector('[data-wall-grid]');
                if (more) {
                    Array.prototype.slice.call(more.children).forEach(function (child) { g.appendChild(child); });
                    g.dataset.page = String(page + 1);
                    if (window.DekorrasWall) window.DekorrasWall.markCards(g);
                    // URL'de sayfa numarası güncellenmez: yenilemede baştan başlamak beklenen davranış.
                }
            })
            .catch(function () { if (pagination) pagination.hidden = false; })
            .finally(function () { busy = false; if (loading) loading.classList.add('d-none'); });
    }

    var observer = null;
    function observe() {
        if (!('IntersectionObserver' in window) || !sentinel) return;
        if (!observer) {
            observer = new IntersectionObserver(function (entries) {
                if (entries.some(function (e) { return e.isIntersecting; })) loadMore();
            }, { rootMargin: '400px' });
        }
        observer.disconnect();
        observer.observe(sentinel);
        if (pagination) pagination.hidden = true;
    }

    var searchTimer = null;
    form.addEventListener('input', function (e) {
        if (e.target.name !== 'q') return;
        clearTimeout(searchTimer);
        searchTimer = setTimeout(function () { refresh(true); }, 350);
    });
    form.addEventListener('change', function (e) {
        if (e.target.name === 'q' || e.target === colorPicker) return;
        refresh(true);
    });
    document.getElementById('wallSort')?.addEventListener('change', function () { refresh(true); });
    form.addEventListener('submit', function (e) { e.preventDefault(); refresh(true); });

    if (colorPicker && colorValue) {
        var colorTimer = null;
        colorPicker.addEventListener('input', function () {
            colorValue.value = colorPicker.value;
            if (colorClear) colorClear.classList.remove('d-none');
            clearTimeout(colorTimer);
            colorTimer = setTimeout(function () { refresh(true); }, 250);
        });
    }
    if (colorClear) {
        colorClear.addEventListener('click', function () {
            colorValue.value = '';
            colorClear.classList.add('d-none');
            refresh(true);
        });
    }

    form.querySelector('[data-wall-reset]')?.addEventListener('click', function (e) {
        e.preventDefault();
        form.reset();
        form.querySelectorAll('input[type=checkbox]').forEach(function (c) { c.checked = false; });
        form.querySelectorAll('input[type=radio][value=""]').forEach(function (r) { r.checked = true; });
        form.querySelector('input[name=q]').value = '';
        var category = form.querySelector('[data-wall-category]');
        if (category) category.value = '';
        if (colorValue) colorValue.value = '';
        if (colorClear) colorClear.classList.add('d-none');
        var sort = document.getElementById('wallSort');
        if (sort) sort.value = '';
        refresh(true);
    });

    // Geri/ileri: formu URL'den geri yükleyip listeyi yenile.
    window.addEventListener('popstate', function () {
        var params = new URLSearchParams(location.search);
        form.querySelectorAll('input[name=tag]').forEach(function (c) { c.checked = params.getAll('tag').indexOf(c.value) >= 0; });
        ['type', 'orientation'].forEach(function (name) {
            var value = params.get(name) || '';
            form.querySelectorAll('input[name=' + name + ']').forEach(function (r) { r.checked = r.value === value; });
        });
        form.querySelector('input[name=q]').value = params.get('q') || '';
        var categorySel = form.querySelector('[data-wall-category]');
        if (categorySel) categorySel.value = params.get('category') || '';
        if (colorValue) colorValue.value = params.get('color') || '';
        var sort = document.getElementById('wallSort');
        if (sort) sort.value = params.get('sort') || '';
        refresh(false);
    });

    // Dokunmatik cihazlarda ilk dokunuş sahne görünümünü gösterir, ikinci dokunuş ürüne gider.
    results.addEventListener('touchstart', function (e) {
        var media = e.target.closest('.wall-card-media');
        if (!media || !media.querySelector('.wall-card-scene')) return;
        if (!media.classList.contains('is-scene')) {
            e.preventDefault();
            results.querySelectorAll('.wall-card-media.is-scene').forEach(function (m) { m.classList.remove('is-scene'); });
            media.classList.add('is-scene');
        }
    }, { passive: false });

    observe();
})();
