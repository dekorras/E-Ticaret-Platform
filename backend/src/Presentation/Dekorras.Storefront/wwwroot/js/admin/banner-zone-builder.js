// BannerZoneBuilder.razor için sürükle-bırak ağaç düzenleyici - vendored SortableJS (bkz.
// wwwroot/admin-assets/libs/sortablejs/Sortable.min.js, nestable.init.js'deki kullanım deseniyle
// aynı ayarlar: animation 150, fallbackOnBody true, swapThreshold .65) üzerine ince bir JS interop
// katmanı. Satırlar "banner-rows" grubunda, kolonlar "banner-cols" grubunda taşınır - bu ayrım
// Application katmanındaki MoveBannerNodeCommand'in "satırlar yalnızca kolonların, kolonlar
// yalnızca satırların içine taşınabilir" kuralıyla eşleşir.
(function () {
    // Aynı konteyner elementine birden fazla kez initSortable çağrılabilir (her ağaç yeniden
    // yüklemesinden sonra BannerZoneBuilder.OnAfterRenderAsync tüm konteynerleri yeniden tarar) -
    // bu yüzden elementin üzerinde önceki bir Sortable örneği varsa önce onu yok ediyoruz, aksi
    // halde aynı elementte birikmiş birden fazla sürükle-bırak dinleyicisi tekrarlanan/çakışan
    // OnNodeMoved çağrılarına yol açardı.
    window.dekorrasBannerBuilder = {
        initSortable: function (containerId, groupName, dotNetHelper) {
            var el = document.getElementById(containerId);
            if (!el) return;

            if (el.dekorrasSortableInstance) {
                el.dekorrasSortableInstance.destroy();
                el.dekorrasSortableInstance = null;
            }

            el.dekorrasSortableInstance = new Sortable(el, {
                group: groupName,
                animation: 150,
                fallbackOnBody: true,
                swapThreshold: 0.65,
                onEnd: function (evt) {
                    var nodeId = evt.item.getAttribute('data-node-id');
                    if (!nodeId) return;

                    var newParentId = evt.to.getAttribute('data-parent-id') || null;
                    var newIndex = evt.newIndex;
                    var oldIndex = evt.oldIndex;

                    // KRİTİK - GERÇEK kullanıcı ekran görüntüsüyle bulunan çökme: SortableJS bırakma
                    // anında `evt.item`ı GERÇEK DOM'da FİZİKSEL olarak taşımış durumda (kendi iç
                    // sürükleme mantığı gereği - bu kütüphanenin normal/beklenen davranışı). Blazor'un
                    // KENDİ render ağacı bu taşımadan HABERSİZ; OnNodeMoved'in tetiklediği bir sonraki
                    // Blazor render'ı (sunucudan gelen YENİ sırayı uygulamak için) kendi ESKİ bildiği
                    // DOM yapısıyla artık SortableJS tarafından değiştirilmiş GERÇEK DOM'u uzlaştırmaya
                    // çalışırken var OLMAYAN bir üst elemente `removeChild` çağırıp "Cannot read
                    // properties of null (reading 'removeChild')" ile TÜM circuit'i çökertiyordu.
                    // Çözüm: SortableJS'in yaptığı GÖRSEL taşımayı BURADA HEMEN geri al - DOM, Blazor'un
                    // son bildiği haliyle AYNEN kalsın; GERÇEK yeniden sıralama, OnNodeMoved'in
                    // tetiklediği sunucu-onaylı Blazor render'ı (DOM'u KENDİ bildiği şekilde, kendi
                    // diff'iyle güncelleyerek) tarafından yapılsın. `evt.from`/`oldIndex` referans
                    // alınarak elementin TAM eski konumuna (aynı veya farklı konteyner fark etmeksizin)
                    // geri konması - iki VDOM çerçevesiyle (React/Vue) SortableJS entegre ederken de
                    // kullanılan standart, kanıtlanmış bir kalıptır.
                    var referenceNode = evt.from.children[oldIndex] || null;
                    if (referenceNode) {
                        evt.from.insertBefore(evt.item, referenceNode);
                    } else {
                        evt.from.appendChild(evt.item);
                    }

                    dotNetHelper.invokeMethodAsync('OnNodeMoved', nodeId, newParentId, newIndex)
                        .catch(function (err) {
                            console.error('dekorrasBannerBuilder: OnNodeMoved çağrısı başarısız oldu.', err);
                        });
                }
            });
        }
    };
})();
