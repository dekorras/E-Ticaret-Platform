// Kategori/Arama sayfalarında sonsuz kaydırma - IntersectionObserver ile sayfa sonuna yaklaşınca
// bir sonraki sayfanın partial'ını fetch edip grid'e ekler. Yeni eklenen kartların Sepete Ekle/
// Karşılaştır/Hızlı Bakış davranışları zaten event delegation (form submit / document click) ile
// çalıştığı için ekstra bir bağlama gerekmez. JS çalışmazsa/IntersectionObserver yoksa `_Pagination`
// geri düşüşü olduğu gibi görünür kalır (bkz. plan §6).
(function () {
    if (!('IntersectionObserver' in window)) return;

    var container = document.querySelector('[data-dk-infinite]');
    if (!container) return;

    var page = parseInt(container.dataset.page, 10);
    var totalPages = parseInt(container.dataset.totalPages, 10);
    var baseUrl = container.dataset.partialUrl;
    var sentinel = container.querySelector('[data-dk-infinite-sentinel]');
    var loading = container.querySelector('[data-dk-infinite-loading]');
    var grid = container.querySelector('[data-product-row]');
    if (!sentinel || !grid || !baseUrl || page >= totalPages) return;

    var paginationFallback = document.querySelector('[data-dk-pagination-fallback]');
    var loadingMore = false;

    var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
            if (entry.isIntersecting && !loadingMore && page < totalPages) {
                loadMore();
            }
        });
    }, { rootMargin: '200px' });
    observer.observe(sentinel);

    if (paginationFallback) paginationFallback.hidden = true;

    function loadMore() {
        loadingMore = true;
        if (loading) loading.classList.remove('d-none');
        var nextPage = page + 1;

        fetch(baseUrl + '&page=' + nextPage)
            .then(function (response) {
                if (!response.ok) throw new Error('infinite scroll fetch failed');
                return response.text();
            })
            .then(function (html) {
                var temp = document.createElement('div');
                temp.innerHTML = html;
                var newRow = temp.querySelector('[data-product-row]');
                if (newRow) {
                    Array.prototype.forEach.call(newRow.children, function (child) {
                        grid.appendChild(child);
                    });
                }
                page = nextPage;
                container.dataset.page = String(page);
                if (page >= totalPages) {
                    observer.unobserve(sentinel);
                }
            })
            .catch(function () {
                // Sessiz başarısızlık - `_Pagination` geri düşüşü sayfa yenilenirse zaten çalışır.
                observer.unobserve(sentinel);
                if (paginationFallback) paginationFallback.hidden = false;
            })
            .finally(function () {
                loadingMore = false;
                if (loading) loading.classList.add('d-none');
            });
    }
})();
