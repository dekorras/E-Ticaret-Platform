// Ürün listeleme sayfası (Kategori/Arama) - "_ProductToolbar" içindeki liste/ızgara görünümü ve
// sütun sayısı (2/3/4) düğmelerini yönetir, tercih tarayıcının localStorage'ında kalıcı tutulur.
// Yalnızca GERÇEK bir toolbar+grid ikilisi olan sayfalarda ([data-dk-product-listing]) çalışır -
// Anasayfa blokları/BannerZone ürün widget'ı/Ürün Detayı "Benzer Ürünler" gibi `_ProductGrid`'in
// diğer kullanım yerlerine KASITLI olarak DOKUNMAZ (bkz. backend/README.md).
(function () {
    var STORAGE_VIEW = 'dkProductView';
    var STORAGE_COLS = 'dkProductCols';

    var listing = document.querySelector('[data-dk-product-listing]');
    if (!listing) return;

    var row = listing.querySelector('[data-product-row]');
    var toolbar = listing.querySelector('[data-dk-product-toolbar]');
    if (!row || !toolbar) return;

    var colsGroup = toolbar.querySelector('[data-dk-cols-group]');

    function safeGet(key) {
        try { return window.localStorage.getItem(key); } catch (e) { return null; }
    }

    function safeSet(key, value) {
        try { window.localStorage.setItem(key, value); } catch (e) { /* yoksay - gizli sekme/engellenmiş depolama */ }
    }

    function applyView(view) {
        row.dataset.view = view;
        toolbar.querySelectorAll('[data-dk-view]').forEach(function (btn) {
            btn.classList.toggle('active', btn.dataset.dkView === view);
        });
        if (colsGroup) colsGroup.classList.toggle('dk-cols-disabled', view === 'list');
    }

    function applyCols(cols) {
        row.dataset.cols = cols;
        toolbar.querySelectorAll('[data-dk-cols]').forEach(function (btn) {
            btn.classList.toggle('active', btn.dataset.dkCols === String(cols));
        });
    }

    var savedView = safeGet(STORAGE_VIEW);
    if (savedView === 'grid' || savedView === 'list') applyView(savedView);

    var savedCols = safeGet(STORAGE_COLS);
    if (savedCols === '2' || savedCols === '3' || savedCols === '4') applyCols(savedCols);

    toolbar.addEventListener('click', function (e) {
        var viewBtn = e.target.closest('[data-dk-view]');
        if (viewBtn) {
            applyView(viewBtn.dataset.dkView);
            safeSet(STORAGE_VIEW, viewBtn.dataset.dkView);
            return;
        }

        var colsBtn = e.target.closest('[data-dk-cols]');
        if (colsBtn) {
            applyCols(colsBtn.dataset.dkCols);
            safeSet(STORAGE_COLS, colsBtn.dataset.dkCols);
        }
    });
})();
