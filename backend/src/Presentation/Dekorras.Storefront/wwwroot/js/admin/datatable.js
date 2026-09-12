// Admin panel liste sayfaları için jQuery DataTables sarmalayıcısı - vanilla IIFE (quickview.js/
// banner-zone-builder.js ile AYNI üslup), yalnızca TEK bir global obje ekler.
//
// KRİTİK DERS (bkz. backend/README.md - "BannerZoneBuilder'da hiçbir buton çalışmıyor"): Blazor
// Server bir liste yeniden yüklendiğinde <table>'ın satırlarını YENİDEN RENDER eder - DataTables
// ise o <table>'ın DOM'unu (sarmalayıcılar, sayfalama, sıralama durumu) KENDİSİ yönetir. Bu ikisi
// ÇAKIŞIR. Çözüm: her başlatmadan ÖNCE var olan bir DataTable örneği varsa YOK EDİLİR, sonra
// SIFIRDAN kurulur - SortableJS için bu oturumda zaten kanıtlanmış "yok et - yeniden kur" deseni.
(function () {
    // DataTables'ın kendi "language" seçeneği - proje tamamen Türkçe olduğu için ayrı bir JSON
    // dosyası/ağ isteği yerine burada GÖMÜLÜ tutuluyor (bkz. plan - "ayrı bir JSON dosyası yok").
    var turkishLanguage = {
        search: "Ara:",
        lengthMenu: "Sayfada _MENU_ kayıt göster",
        info: "_TOTAL_ kayıttan _START_-_END_ arası gösteriliyor",
        infoEmpty: "Kayıt yok",
        infoFiltered: "(_MAX_ kayıt içinden filtrelendi)",
        zeroRecords: "Eşleşen kayıt bulunamadı",
        emptyTable: "Tabloda veri yok",
        paginate: {
            first: "İlk",
            last: "Son",
            next: "Sonraki",
            previous: "Önceki"
        }
    };

    window.dekorrasDataTable = {
        // lastColumnIsActions: sayfaların ÇOĞUNDA son sütun Düzenle/Sil/Aktifleştir gibi işlem
        // butonları taşır (sıralanması/aranması ANLAMSIZ) - ama HEPSİNDE değil (ör. BrandList'in
        // son sütunu gerçek bir veri alanı olan "Durum"). Bu yüzden merkezi ama KOŞULLU bir kural:
        // çağıran taraf (DataTableScript.razor) bu bayrağı sayfaya göre true/false geçer.
        //
        // preserveServerOrder: DataTables, HİÇBİR "order" seçeneği verilmezse KENDİLİĞİNDEN ilk
        // sütuna göre alfabetik sıralar - bu, CategoryList gibi sunucu tarafında BİLİNÇLİ bir sıraya
        // (ör. üst-alt hiyerarşi ön-sıralı gezinme) dizilmiş tablolarda o sırayı ANINDA BOZAR. Bu
        // bayrak true ise `order: []` geçilir - DataTables İLK açılışta hiç sıralama uygulamaz,
        // sunucunun render ettiği satır sırası AYNEN korunur (kullanıcı yine de bir sütun başlığına
        // tıklayarak istediği an sıralayabilir).
        init: function (tableId, lastColumnIsActions, preserveServerOrder) {
            var el = document.getElementById(tableId);
            if (!el) return;

            var $el = $(el);
            if ($.fn.DataTable.isDataTable(el)) {
                $el.DataTable().destroy();
            }

            // ÖNEMLİ: DataTables'ın KENDİ "responsive: true" eklentisi BİLEREK KULLANILMIYOR -
            // dar ekranlarda öncelik sırasına göre sütunları GİZLEYİP bunları bir "+" ile açılan
            // ALT SATIRA taşırken, o hücrelerin İÇERİĞİNİ (Blazor'un @onclick ile YÖNETTİĞİ,
            // circuit'e bağlı butonlar dahil) KLONLAYARAK yeni DOM düğümlerine kopyalıyor - bu
            // klonlar Blazor'un kendi olay-yönlendirme mekanizmasına BAĞLI DEĞİL, bu yüzden
            // tıklandıklarında HİÇBİR ŞEY olmuyordu (gerçek kullanıcı geri bildirimiyle bulunan bir
            // hata - "Anasayfada Göster/Gizle" butonu, ve muhtemelen aynı geniş "İşlemler"
            // sütunundaki DİĞER butonlar da, dar ekranlarda bu şekilde etkisiz kalıyordu). Zaten
            // HER sayfada mevcut olan Bootstrap `.table-responsive` sarmalayıcısı (yatay kaydırma)
            // dar ekran sorununu DataTables'a hiç ihtiyaç duymadan, DOM'u KLONLAMADAN çözüyor.
            var options = {
                language: turkishLanguage
            };
            if (lastColumnIsActions) {
                options.columnDefs = [{ orderable: false, searchable: false, targets: -1 }];
            }
            if (preserveServerOrder) {
                options.order = [];
            }

            $el.DataTable(options);
        },

        // Sayfanın kendi filtre <select>'lerinden çağrılır - belirli bir sütunda TAM EŞLEŞME
        // araması yapar (ör. "Durum" sütununda yalnızca "Aktif" hücrelerini bırakır). Boş/"" değer
        // o sütundaki filtreyi TAMAMEN kaldırır. Tablo zaten init() ile kurulmuş olmalı - burada
        // yeniden OLUŞTURULMAZ, `.DataTable()` argümansız çağrıldığında var olan örneğin API'sini
        // döndürür (DataTables'ın kendi belgelenmiş davranışı).
        filterColumn: function (tableId, columnIndex, exactValue) {
            var el = document.getElementById(tableId);
            if (!el || !$.fn.DataTable.isDataTable(el)) return;

            var table = $(el).DataTable();
            var pattern = exactValue ? "^" + $.fn.dataTable.util.escapeRegex(exactValue) + "$" : "";
            table.column(columnIndex).search(pattern, true, false).draw();
        }
    };
})();
