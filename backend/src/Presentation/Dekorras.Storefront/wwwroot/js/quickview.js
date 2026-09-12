// Ürün kartındaki "Hızlı Bakış" butonu - fetch ile partial'ı çekip zaten vendored Bootstrap 5
// modal'ına basar (yeni bir modal kütüphanesi gerekmez). Progressive enhancement: buton
// bulunamazsa/JS çalışmazsa müşteri normal ürün detay sayfasını ziyaret etmeye devam eder.
(function () {
    document.addEventListener('click', async function (e) {
        var btn = e.target.closest('.dk-quickview-btn');
        if (!btn) return;
        e.preventDefault();

        var modalEl = document.getElementById('dkQuickviewModal');
        if (!modalEl || typeof bootstrap === 'undefined') return;

        var modalBody = modalEl.querySelector('.modal-body');
        modalBody.innerHTML = '<div class="text-center py-5"><div class="spinner-border" role="status"></div></div>';
        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();

        try {
            var returnUrl = window.location.pathname + window.location.search;
            var response = await fetch('/Product/Quickview?slug=' + encodeURIComponent(btn.dataset.slug) + '&returnUrl=' + encodeURIComponent(returnUrl));
            if (!response.ok) throw new Error('quickview fetch failed');
            modalBody.innerHTML = await response.text();
        } catch (err) {
            modalBody.innerHTML = '<p class="text-danger text-center py-5">Ürün yüklenemedi. Lütfen ürün sayfasını ziyaret edin.</p>';
        }
    });
})();
