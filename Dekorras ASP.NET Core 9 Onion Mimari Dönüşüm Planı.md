# Dekorras.com — ASP.NET Core 9 Onion Mimari Dönüşüm Analizi

Mevcut sistem incelemesi, mimari karar seti ve uygulama ekibine/AI asistanına verilecek kapsamlı geliştirme promptu · 1 Eylül 2026

## 1\. Yönetici Özeti

**dekorras.com**, Kayseri merkezli bir iç/dış cephe dekorasyon malzemeleri üreticisinin (söve, duvar kaplaması, duvar kağıdı, taş desen panel, kartonpiyer, poster vb.) e\-ticaret sitesidir. Site ve yönetim paneli, doğrudan incelenerek tespit edildiği üzere **KobiDirekt** adlı Türkiye merkezli bir e\-ticaret SaaS platformu üzerinde çalışmaktadır; bu platform **OpenCart** çekirdeği ve **Journal 3** teması üzerine kurulu, PHP 7.3 ve MariaDB ile çalışan, IonCube ile şifrelenmiş kapalı kaynaklı bir sistemdir. Ayrıca firmanın **BizimHesap** adlı bir ön muhasebe SaaS'ı üzerinden yürüttüğü, cari hesap/stok/fatura/e\-Fatura işlemlerini kapsayan ayrı bir muhasebe sistemi olduğu da tespit edilmiştir (bkz. §2.8).

Katalog **\~3.100 ürün ve 103 kategoriden** oluşan büyük ve derinlemesine dallanmış bir yapıya sahipken, sipariş hacmi düşüktür (panelde görünen 9 sipariş, 110 müşteri). Buna karşın muhasebe tarafında **389 cari hesap (müşteri/tedarikçi)** ve son bir ay içinde yüzlerce satış/fatura kaydı bulunması, firmanın esas iş hacminin şu an **B2B/saha satışı ve BizimHesap üzerinden** yürüdüğünü, web sitesinin ise görece küçük bir kanal olduğunu göstermektedir. Bu, projeyi yalnızca bir "e\-ticaret sitesi yenileme" değil, **e\-ticaret \+ muhasebe/ERP'nin birleştiği bir orta ölçekli iş sistemi** olarak konumlandırır.

Bu belge üç bölümden oluşur: **(A)** mevcut e\-ticaret sitesi ve admin panelinin, **(B)** mevcut BizimHesap ön muhasebe sisteminin madde madde teknik analizi, **(C)** bu analizlere ve aşağıdaki onaylanmış mimari kararlara dayanarak hazırlanmış, bir yazılım ekibine veya bir AI kodlama asistanına doğrudan verilebilecek **kapsamlı geliştirme promptu**.

**Onaylanan mimari kararlar:** (1) Storefront — Server\-Side Rendering (SSR), (2) Admin panel — Blazor (Server), (3) Veritabanı — Microsoft SQL Server, (4) Ödeme, kargo, pazaryeri ve e\-Fatura sağlayıcılarının TÜMÜ, admin panelden **dinamik giriş ekranlarıyla** — kod değişikliği/deploy gerektirmeden — sonradan aktifleştirilebileceği bir **"Sağlayıcı Kayıt Defteri" (Provider Registry)** mimarisiyle kurgulanacak (bkz. §4.1), (5) Sistem **e\-ihracata** (çok dil: TR/EN \+ DE/FR/NL/ES/AR, çok para birimi, uluslararası kargo/fatura) uygun tasarlanacak, (6) Mobil uygulamalar için ortak bir **API katmanı** bulunacak, (7) Bu API'yi tüketen **Flutter tabanlı bir mobil uygulama** da geliştirilecek, (8) Sistem **Trendyol, Hepsiburada, N11, İdefix, Amazon** gibi hazır pazaryerleriyle **çift yönlü entegrasyon** kurabilecek, her biri admin panelden isteğe bağlı aktifleştirilecek şekilde tasarlanacak, (9) **BizimHesap'ın yapısından esinlenilen native bir Muhasebe modülü** eklenecek ve admin girişinde kullanıcı **Muhasebe Modülü** ile **E\-Ticaret Modülü** arasında seçim yapabileceği bir ekranla karşılanacak.

## 2\. Mevcut Sistem Analizi

### 2\.1 Platform ve Alt Yapı Tespiti

| Katman | Tespit |
| --- | --- |
| E\-ticaret platformu | **KobiDirekt** (admin girişinde "KobiDirekt Yönetim Paneli" ibaresi, footer'da "KobiDirekt E\-ticaret ile kurulmuştur") |
| Alt yapı çekirdeği | OpenCart tabanlı — admin URL şeması birebir OpenCart route yapısı (`route=catalog/product`, `route=sale/order`, `route=catalog/category`, `route=customer/customer`, `route=catalog/information`, `route=catalog/manufacturer`) |
| Tema | **Journal 3** (Ayarlar \> Genel \> Tema Seçiniz alanında "Journal Theme v3" olarak görünüyor — OpenCart'ın en bilinen ücretli temalarından biri) |
| Sunucu / Dil | PHP **7\.3.33** (Ocak 2021'den beri güvenlik desteği yok / EOL), LiteSpeed web sunucu, paylaşımlı barındırma (hayalhost.com) |
| Veritabanı | MariaDB **10\.5.29** |
| Kaynak koruma | **IonCube 10.4** ile şifrelenmiş çekirdek/eklenti dosyaları — kaynak kod kapalı, platformdan bağımsız taşınamıyor |
| Sunucu limitleri | `memory_limit: 64M`, `upload_max_filesize: 999M`, `max_execution_time: 36000` |
| Eklenti pazarı | 94 modül, **67 ödeme yöntemi**, 15 kargo yöntemi, 19 sipariş toplamı eklentisi, 13 rapor, 2 tema — OpenCart'ın standart "Marketplace" eklenti kataloğu |

**Sonuç:** Dekorras'ın kendine ait, taşınabilir bir kod tabanı yoktur; site tamamen üçüncü taraf bir SaaS'a kilitlenmiştir. Bu, projenin en güçlü gerekçesidir: front\-end ve admin panelin **sıfırdan** ASP.NET Core 9 ile yazılması, platform bağımsızlığı, kaynak kodu sahipliği ve modern bir mimari kazandıracaktır.

### 2\.2 Front\-End (Mağaza) Analizi

**Bilgi mimarisi / üst menü:**

| Ana kategori | Örnek alt kategoriler (gözlemlenen) |
| --- | --- |
| Duvar Kaplamaları | Sedir Tuğla, Barok Taş, Avanos Taş, Kale Taşı, Ürgüp Taşı, Loft Tuğla, Polimer Lambri Panel, Polimer Çıtalar |
| Dış Cephe Dekor Ürünleri | Kat Silmesi, Saçak Silmesi, Fuga Profilleri, Söveler, Sütunlar, Cephe Kaplamalar, Denizlikler, Harpuştalar, Köşe Taşları |
| İç Cephe Ürünleri | 3D Paneller, Strafor Taş Desen Paneller, Lamba Göbekleri, Kartonpiyer, Gizli Işık, Duvar Çıtası |
| Posterler | Doğa, Araba, Hayvan, Spor, Şehir, Deniz posterleri |
| Duvar Kağıtları | Adawall, Decowall, GMZ, Afra, Erva, Livart (marka/koleksiyon bazlı alt menüler) |
| Kurumsal | Hakkımızda, Gizlilik Politikası, Mesafeli Satış Sözleşmesi, İade ve İptal Koşulları, İletişim |

Admin panelindeki kategori ağacı bu menüyle tutarlı biçimde toplam **103 kategoriye**, yer yer 3 seviyeye kadar (örn. *Dış Cephe Dekor Ürünleri \> Strafor Taş Desen Paneller \> Loft Tuğla Panel Kaplamalar*) derinleşen bir hiyerarşiye sahiptir.

**Ürün listeleme sayfası:** sıralama (varsayılan, ad A\-Z/Z\-A, ucuzdan\-pahalıya, pahalıdan\-ucuza, puana göre, ürün koduna göre), sayfa başına gösterim (12/25/50/75/100), ürün karşılaştırma listesi, liste üzerinden hızlı "Sepete Ekle".

**Ürün detay sayfası:** KDV dahil/hariç fiyat gösterimi, stok durumu ("VAR"/"YOK"), marka, ürün kodu, "Sepete Ekle" / "Hemen Al", **minimum satış adedi** kısıtı (görülen örnekte "asgari adet: 10" — toptan/B2B satış mantığının işaretidir), zengin metin ürün açıklaması, İptal & İade sekmesi, Ürün Yorumları, Taksit seçenekleri, Soru/Cevap sekmesi.

**Üyelik ve sepet:** e\-posta/şifre ile üye girişi ve kayıt, sipariş geçmişi, favori listesi, destek merkezi; ayarlarda **misafir alışverişi** desteği aktif olarak görülmüştür.

**İçerik pazarlaması:** "Blog Yazılarım" altında yerel SEO'ya yönelik makaleler (ör. *Kayseri Söve İmalatı*, *Kayseri Dış Cephe Söve Uygulamaları*, *Pencere Sövesi Kayseri*) — yerel arama motoru görünürlüğü stratejisinin sitenin önemli bir parçası olduğunu gösterir.

**İletişim kanalları:** sabit telefon hattı, WhatsApp Business linki, Instagram, Telegram — footer/header üzerinden doğrudan erişim.

**Diğer:** Sağ üstte dil seçici (şu an tek dil TR aktif görünüyor, alt yapı çok dilliliğe hazır), para birimi sabit TL, tüm yasal metinler (Gizlilik Politikası, Mesafeli Satış Sözleşmesi, İade/İptal Koşulları) Türkiye e\-ticaret mevzuatına uygun hazır şablonlar olarak mevcut.

### 2\.3 Admin Panel Analizi

Giriş sonrası kontrol paneli (dashboard) şu anlık metrikleri gösteriyor:

| Metrik | Değer |
| --- | --- |
| Toplam Sipariş | 9 |
| Toplam Satış | 6\.3K TL |
| Toplam Kategori | 103 |
| Toplam Müşteri | 110 |
| Toplam Ürün | \~3.100\+ (liste erişiminde ilk ürün ID'si 1517'den başlıyor, aktif ürün sayısı 3.1K olarak raporlanıyor) |
| Çevrimiçi Ziyaretçi | 36 |

Dashboard ayrıca Türkiye haritası üzerinde bölgesel sipariş/müşteri dağılımı, sipariş/müşteri trend grafiği, "son etkinlikler" akışı (yeni müşteri kayıtları) ve "son siparişler" tablosunu içeriyor.

**Üst menüdeki 10 ana modül:**

| \# | Modül | İçerik / Alt Yapı |
| --- | --- | --- |
| 1 | Giriş | Dashboard (yukarıdaki metrikler) |
| 2 | Ürünler | Tam OpenCart tipi ürün kataloğu yönetimi (bkz. §2.4) |
| 3 | Pazaryeri | **Çoklu satıcı (marketplace) modülü v4.1.2.0** — şu an kapalı, ama satıcı kaydı, otomatik onay, komisyon, ayrı satıcı paneli, satıcı\-müşteri mesajlaşması gibi tam bir B2B2C alt yapısı hazır durumda. *(Not: bu, Dekorras'ın kendisinin bir pazaryeri platformuna dönüşmesi için bir modüldür — §9'da ele alınan, Dekorras'ın hazır pazaryerlerinde SATICI olarak yer alması konusundan farklıdır.)* |
| 4 | Sayfa Yönetimi | CMS — "Bilgi Sayfaları" (6 statik sayfa: Gizlilik Politikası, Hakkımızda, İptal ve İade Koşulları, Mesafeli Satış Sözleşmesi, Ön Bilgilendirme Formu, Satış Sözleşmesi), her sayfanın kendi SEO alanı var |
| 5 | Satış | Sipariş listesi ve yönetimi (bkz. §2.5) |
| 6 | Tasarım | Journal 3 sürükle\-bırak tema/sayfa düzenleyicisi |
| 7 | Kampanya & Pazarlama | "Gelişmiş Promosyon Modülü" — kural bazlı kampanya oluşturucu, misafir/üye kullanım limiti, kullanım sayacı, sıralama |
| 8 | Entegrasyonlar | Bu hesaba kapalı/yetkisiz — ödeme yöntemleri arasında görülen "İyzico Pazaryeri Entegrasyonu" eklentisi de dikkate alındığında, muhtemelen kargo, e\-fatura/muhasebe ve **pazaryeri (Trendyol vb.) senkronizasyon** entegrasyonlarını barındırıyor |
| 9 | Uygulamalar | Eklenti pazarı — 94 modül, 67 ödeme yöntemi, 15 kargo yöntemi, 19 "sipariş toplamı" eklentisi (kupon, hediye çeki, vergi vb.), 13 rapor, 2 tema |
| 10 | Ayarlar | Genel Ayarlar — Genel / Mağaza / Yerel / Seçenek / Resim / E\-Posta / Sunucu sekmeleri (bkz. §2.6) |

Ayrıca üst menüde bağımsız olarak **Müşteriler** modülü bulunuyor: müşteri listesinde ad, e\-posta, telefon, haber bülteni aboneliği, **müşteri grubu (Bireysel / Kurumsal)**, durum, IP adresi, **ortaklık (affiliate) durumu** ve kayıt tarihi alanları yer alıyor.

### 2\.4 Ürün Veri Modeli (Detaylı)

Ürün düzenleme ekranı 11 sekmeden oluşuyor; bu, hedef `Product` entity'sinin ne kadar zengin olması gerektiğini doğrudan gösteriyor:

| Sekme | Alanlar |
| --- | --- |
| Genel | Ürün adı (dil bazlı), zengin metin açıklama (WYSIWYG) |
| Detay | Ürün Kodu, SKU, UPC, EAN, JAN, ISBN, MPN kodları; Konum; Fiyat; Vergi Sınıfı (KDV %20 / %10); Adet; Minimum Adet; Stoktan Düş (E/H); Stok Dışı Durumu (2\-3 gün içinde / Ön Sipariş / Stokta var / Stokta yok); Kargo Gerekli; Yayına Girme Tarihi; Boyutlar (U×G×Y) \+ birim; Ağırlık \+ birim; Durum (Açık/Kapalı); Meta Robots; **Google Merchant** Age Group / Gender alanları (Google Shopping feed'e hazır alt yapı); Sıralama |
| Bağlantılar | Kategori(ler), Marka/Üretici, ilgili ürünler |
| Özellik | Ürün özellikleri (attribute) — teknik özellik tabloları |
| Seçenek | Ürün seçenekleri/varyantları (ör. ölçü, renk) |
| Adet İndirimi | Miktar bazlı kademeli fiyatlandırma (toptan satış mantığı) |
| Kampanya | Ürüne özel kampanya tanımları |
| Ürün Video | Ürün videosu ekleme |
| Resim | Çoklu ürün görseli |
| Puan | Sadakat/ödül puanı |
| Custom field | Serbest özel alanlar |
| Tasarım | Ürüne özel sayfa düzeni override'ı |

**Kategori/marka verisi:** 103 kategori (çoğu 2\-3 seviyeli), **8 marka/üretici**\: ADAWALL, DECOWALL, DEKORRAS, GMZ WALPAPERS, KARODEX, LİVART, NASOL, VERTU.

### 2\.5 Sipariş, Ödeme ve Kargo

**Sipariş durum makinesi** (Genel Ayarlar \> Mağaza sekmesinde tanımlı) tam **14 farklı durum** içeriyor: Onay Bekliyor, Hazırlanıyor, Hazırlandı, Kargoya Verildi, Tamamlandı, İptal Edildi, İptal Geri Alındı, İade Edildi, Reddedildi, Süresi Doldu, Ters İbraz, Başarısız, Durduruldu, Hükümsüz. Ayrıca "sahtekârlık sipariş durumu" ayrı olarak tanımlanabiliyor. Sipariş listesinde barkod yazdırma, kargo takip açma, filtreleme (sipariş no, müşteri, durum, tutar, tarih aralığı) mevcut.

**Ödeme yöntemleri:** 67 hazır entegrasyon arasında şu an **aktif** olanlar "Banka Havale/EFT" ve "Param POS Payment"; ayrıca kullanılabilir durumda Türkiye'ye özgü sağlayıcılar: **iyzico, PayTR, Shopier, Hepsipay, MokaPos, weePay, EsnekposPos, Paynet SanalPOS**, kapıda nakit/kredi kartı ödeme, çek/posta havalesi, PayPal ailesi.

**Kargo:** 15 hazır kargo eklentisi mevcut (bu hesaba detay erişimi kısıtlı); mağaza ürün sayfalarında "Kargo Alıcı Ödemeli" notu standart olarak kullanılıyor.

### 2\.6 Genel Ayarlar, Pazarlama ve Uyumluluk

- **Müşteri grupları:** Bireysel ve Kurumsal (B2C/B2B ayrımı); ürün fiyatlarını sadece belirli gruplara gösterme opsiyonu mevcut.
- **Ortaklık (Affiliate) sistemi:** komisyon oranı yüzdesi, otomatik/manuel onay, referans bazlı satış takibi.
- **Hediye çeki (gift voucher)** desteği, minimum/maksimum tutar sınırı.
- **Doğrulama:** Google reCAPTCHA — kayıt, misafir alışveriş, yorum, ürün iadesi ve iletişim formlarında ayrı ayrı açılıp kapatılabiliyor.
- **Vergi:** Vergi sınıfları ve mağaza/müşteri bazlı varsayılan vergi adresi (teslimat/fatura) ayrımı.
- **SEO:** Sayfa/ürün bazlı meta başlık, açıklama, anahtar kelime; meta robots; Google Merchant alanları.
- **Kampanya motoru:** "Gelişmiş Promosyon Modülü" ile kural bazlı, tarih aralıklı, misafir/üye kullanım limitli kampanyalar.
- **Yasal uyum:** Mesafeli Satış Yönetmeliği ve KVKK'ya uygun hazır sözleşme/politika şablonları CMS sayfaları olarak yönetiliyor.

### 2\.7 Ölçek Özeti (Migrasyon Planlaması İçin)

| Varlık | Miktar |
| --- | --- |
| Kategori | 103 |
| Ürün | \~3.100\+ |
| Marka | 8 |
| Müşteri (e\-ticaret) | 110 |
| Sipariş (görüntülenen) | 9 |
| Statik/CMS sayfası | 6 |
| Aktif ödeme yöntemi | 2 (67 hazır seçenek arasından) |
| Cari hesap (BizimHesap) | 389 |

**Yorum:** Katalog büyük ve derin (103 kategori × \~3.100 ürün), e\-ticaret sitesi üzerinden sipariş/müşteri hacmi henüz düşük, ancak **BizimHesap tarafında 389 cari hesap ve yoğun günlük fatura/irsaliye trafiği** bulunuyor. Bu, migrasyon riskinin ağırlıklı olarak **(a)** e\-ticaret ürün/kategori/görsel veri taşıma ve **(b)** muhasebe tarafındaki cari hesap/stok/fatura geçmişinin doğru şekilde yeni sisteme aktarılması olduğunu gösteriyor.

### 2\.8 BizimHesap (Ön Muhasebe) Analizi

Verilen giriş bilgileriyle (`artisanat@hotmail.com`) **bizimhesap.com** hesabına giriş yapılarak incelenmiştir. Hesap altında birden fazla firma kaydı (ESKİ MUHASEBE, TURAN YILMAZ RESMİ HESAP, **DEKORRAS MİMARLIK MÜHENDİSLİK**, ORTAKLIK) bulunmakta olup analiz Dekorras'a ait firma üzerinden yapılmıştır.

**Genel yapı — sol menü:** Ana Sayfa, BizimSipariş, BizimMuhasebeci, Müşteriler, Tedarikçiler, Ürünler, Satışlar, Alışlar, Teklifler, Nakit Yönetimi, E\-Ticaret, Avantajlar, Ayarlar, Raporlar, Yardım.

**Dashboard (Ana Sayfa):** Bugünkü Satış, Bugünkü Tahsilat, 30 Günlük Ciro, 30 Günlük Masraf, Stok Değeri özet kartları; ardından **Varlıklar** (Kasa, POS, Banka, Çek, Senet, Stok, Açık Hesap/alacaklar, Çalışanlar) ve **Borçlar** (Açık Hesap/borçlar, Senet, Kredi Kartı) kırılımlı bilanço görünümü; Yaklaşan Çek/Senetler ve Yaklaşan Masraflar takvimi; duyurular ve son işlemler akışı. İncelenen canlı veride toplam varlıklar **30\.760.727,05 TL**, toplam borçlar **2\.888.150,49 TL** olarak görüntülenmiştir.

**Ürünler:** Ürün/Hizmet Tanımı, Depolar (çoklu depo desteği), Üretim (üretim/BOM benzeri bir modül), Özel Fiyat Listeleri, Kataloglarınız, Ürün Varyantları. Ürün listesinde dikkat çeken nokta: stok miktarları yalnızca "adet" değil, **"m²" (metrekare) ve "mt" (metre tül)** gibi birimlerle de tutulabiliyor — bu, Dekorras'ın sövelik/kaplama gibi metraj bazlı sattığı ürünlerle birebir örtüşüyor ve e\-ticaret tarafındaki `Product` modelinin de birim (UoM) alanı taşıması gerektiğini doğruluyor. Toplu Excel'den ürün yükleme/güncelleme/resim yükleme/silme araçları mevcut.

**Satışlar:** Perakende Satış Gir / Yeni Müşteriyle Satış Gir / Kayıtlı Müşteriyle Satış Gir seçenekleriyle hızlı satış girişi; her kayıt bir **Durum** taşıyor: *Taslak*, *İrsaliyeleşmiş*, *Faturalaşmış*, **Faturalaşmış (E\-Fatura)**, **Faturalaşmış (E\-Arşiv)** — yani sistem resmi **GİB e\-Fatura/e\-Arşiv** entegrasyonuna sahip ve belge numaralarını (ör. `AAA2026000000061`) otomatik yönetiyor. Satış kayıtlarında ayrıca bir **"Sipariş No"** sütunu bulunuyor (ör. `260901-1`) — bu, e\-ticaret/saha siparişlerinin doğrudan muhasebe kaydına bağlandığının somut kanıtıdır ve hedef sistemde **Order → Invoice** akışının otomatikleştirilmesi gerektiğini gösterir.

**Müşteriler (Cari Hesap):** 389 kayıtlı cari; her kayıt İsim/Unvan, telefon, **Açık Bakiye** (güncel cari bakiye) ve **Çek/Senet Bakiyesi** taşıyor. Toplu Excel'den müşteri yükleme/güncelleme mevcut.

**E\-Ticaret modülü (BizimHesap'ın kendi modülü):** Satışlar, Mutabakat, İstatistikler, Ayarlar, Müşteri Soruları, **Ürün Eşleştirme**, Listeleme, Fiyat Güncelleme alt menüleriyle, incelenen "Ayarlar" ekranında **Trendyol, Hepsiburada, Temu, Amazon, N11, Çiçeksepeti, Pazarama, PTT AVM, Trendyol Hızlı Market, Trendyol Yemek, Flo, AliExpress, Ozon, Hepsiglobal (e\-ihracat), Etsy, Joom, Wish, Wix, Beymen, Turkcell Pasaj** ve daha fazlası dahil onlarca hazır pazaryeri/platform entegrasyonu listeleniyor (kategoriler: Pazaryeri, E\-ticaret, E\-ihracat, Medikal, E\-ticaret Uygulamaları, Yemek&Market, Diğer). Bu, hem §9'daki pazaryeri entegrasyon mimarisinin (ürün eşleştirme, fiyat güncelleme, sipariş senkronu) doğru kurgulandığını doğruluyor, hem de firmanın e\-ihracat (Hepsiglobal, AliExpress, Etsy, Ozon gibi kanallar üzerinden) konusunda zaten bir farkındalığı/niyeti olduğunu gösteriyor.

**Ayarlar:** Kullanıcılar (çoklu kullanıcı/yetkilendirme), Tanımlar, **E\-Fatura**, e\-Fatura POS Ayarları, Teklif ve Özel Şablonlar, Etiket Şablonları, SMS Ayarları, **Kargo Entegrasyonu**.

**API erişimi (ek bulgu):** BizimHesap, `apidocs.bizimhesap.com` adresinde genel bir entegrasyon API'si yayınlıyor; belgelenen uç noktalar arasında **Sipariş/Fatura Ekleme**, **Ürün Listesi Alma**, **Depoların Listesi Alma** ve **Depo Stoğu Getirme** yer alıyor. Bu, BizimHesap'ın en azından sipariş→fatura ve stok senkronu için programatik olarak erişilebilir olduğunu gösteriyor; ancak API anahtarı edinme koşulları, plan/paket kısıtları ve resmi e\-Fatura/e\-Arşiv'in bizzat bu API üzerinden mi kesildiği dokümantasyonda açık değil — bu nokta BizimHesap destek/satış ekibiyle teyit edilmelidir (bkz. §10.3 ve §15).

**Değerlendirme — kullanım kolaylığı:** BizimHesap'ın en güçlü yanı, karmaşık muhasebe kavramlarını (cari, irsaliye, fatura, e\-Fatura/e\-Arşiv ayrımı, kasa/banka/çek/senet) sade, tek tıkla erişilen ekranlar ve net durum etiketleri (Taslak / İrsaliyeleşmiş / Faturalaşmış) ile sunmasıdır. Hızlı satış girişi (perakende/kayıtlı müşteri ayrımı), dashboard'daki varlık\-borç özeti ve toplu Excel içe/dışa aktarım araçları, günlük operasyonu hızlandıran somut UX kararlarıdır — hedef Muhasebe modülünün tasarımında bu kalıpların korunması önerilir (bkz. §10).

## 3\. Mevcut Platformdan Ayrılma Gerekçeleri

1. **PHP 7.3 EOL** — Ocak 2021'den beri resmi güvenlik yaması almıyor; bilinen CVE'lere açık kalma riski sürüyor.
2. **IonCube ile şifrelenmiş kapalı kaynak** — platformun kendi eklentileri/çekirdeği incelenip özelleştirilemiyor; şirket kendi yazılımının sahibi değil.
3. **Üçüncü taraf SaaS bağımlılığı** — abonelik/sözleşme sona erdiğinde erişim ve veri riski; yeni özellik geliştirmek bütünüyle KobiDirekt'in eklenti pazarına bağımlı.
4. **Kısıtlı sunucu kaynağı** — `memory_limit: 64M` gibi paylaşımlı hosting sınırları, büyüyen bir kataloğun (3.100\+ ürün) performansını tehdit ediyor.
5. **Modern mühendislik pratiklerinin yokluğu** — otomatik test, CI/CD, API\-first tasarım, sürüm kontrolü altında bir kod tabanı yok.
6. **Sahip olunamayan veri modeli** — özel iş kuralları (ör. min. adet ile toptan satış, Bireysel/Kurumsal fiyatlandırma) platformun genel şablonlarına sıkışmış durumda; büyüme ile birlikte esneklik ihtiyacı artacak.
7. **Genişleme kısıtı** — mevcut platform e\-ihracat, çoklu para birimi/dil, hazır pazaryerleriyle merkezi omnichannel yönetim ve yerel bir Flutter mobil uygulamayı doğal olarak desteklemiyor; bu hedefler ancak kendi kod tabanınızla mümkün.
8. **İki ayrı sistem arasında kopukluk** — e\-ticaret (KobiDirekt) ve muhasebe (BizimHesap) bugün birbirinden bağımsız iki SaaS'tır; sipariş\-fatura eşlemesi (Sipariş No sütunu) manuel/yarı otomatik yürüyor gibi görünmektedir. Tek bir mimari altında birleştirmek, çift veri girişini ve tutarsızlık riskini ortadan kaldırır.

## 4\. Hedef Mimari: ASP.NET Core 9 ile Onion Architecture

Onion (soğan) mimaride bağımlılıklar her zaman **içe doğru** akar: dış katmanlar iç katmanlara bağımlıdır, tersi asla değildir. Domain katmanının hiçbir dış bağımlılığı olmaz.

```text
                ┌─────────────────────────────────────────┐
                │         Presentation / WebUI             │
                │  Dekorras.Api · Dekorras.Admin (Blazor,  │
                │  Modül Seçim Ekranı: Muhasebe/E-Ticaret) │
                │  · Dekorras.Storefront (SSR)              │
                └───────────────┬───────────────────────────┘
                                │ bağımlı
                ┌───────────────▼───────────────────────────┐
                │            Infrastructure                  │
                │  EF Core, Repository/UoW, Ödeme/Kargo/      │
                │  Pazaryeri/e-Fatura adaptörleri (Provider   │
                │  Registry — bkz. §4.1), Cache               │
                └───────────────┬───────────────────────────┘
                                │ bağımlı
                ┌───────────────▼───────────────────────────┐
                │              Application                   │
                │  CQRS (MediatR) — E-Ticaret ve Muhasebe     │
                │  use case'leri, DTO, Validasyon, Mapping     │
                └───────────────┬───────────────────────────┘
                                │ bağımlı
                ┌───────────────▼───────────────────────────┐
                │                Domain                       │
                │  Katalog/Sipariş/Ödeme… ile birlikte         │
                │  Muhasebe (Cari/Fatura/Kasa) entity'leri —   │
                │  SIFIR dış bağımlılık                        │
                └─────────────────────────────────────────────┘

  ┌───────────────────────┐  ┌───────────────────────────┐  ┌───────────────────────────┐
  │ Dekorras.Mobile        │  │ Dekorras.MarketplaceSync   │  │ e-Fatura Entegratör API'si │
  │ (Flutter)              │  │ (opsiyonel Worker Service)  │  │ (sertifikalı 3. parti —    │
  │ → Dekorras.Api         │  │ → Trendyol/Hepsiburada/…    │  │ bkz. §10.3)                │
  └───────────────────────┘  └───────────────────────────┘  └───────────────────────────┘
```

**Onaylanan kararlar (bu belge itibarıyla kesinleşmiştir):**

| Karar alanı | Seçim |
| --- | --- |
| Storefront render stratejisi | **Server\-Side Rendering (SSR)** — ASP.NET Core MVC/Razor Pages, SEO'nun (mevcut yerel arama/blog yatırımı) korunması önceliğiyle |
| Admin panel teknolojisi | **Blazor (Server)** — girişte **Muhasebe / E\-Ticaret modül seçim ekranı** ile |
| Veritabanı | **Microsoft SQL Server** |
| Ödeme / Kargo / Pazaryeri / e\-Fatura mimarisi | **Dinamik Sağlayıcı Kayıt Defteri (Provider Registry)** — her sağlayıcı admin panelden metadata bazlı bir form ile, kod değişikliği olmadan girilip aktifleştirilir; ödeme seçiminde son karar müşteriye aittir — bkz. §4.1 |
| Uluslararası satış | **E\-ihracat uyumlu** tasarım (TR/EN \+ DE/FR/NL/ES/AR, çok para birimi, uluslararası kargo/fatura) — bkz. §7 |
| Mobil erişim | Ortak bir **REST API katmanı** \+ bu API'yi tüketen **Flutter tabanlı mobil uygulama** — bkz. §8 |
| Pazaryeri entegrasyonu | **Trendyol, Hepsiburada, N11, İdefix, Amazon** — her biri isteğe bağlı, admin panelden dinamik aktifleştirilir — bkz. §9 |
| Muhasebe | **BizimHesap'tan esinlenilen native Muhasebe modülü** \+ sertifikalı entegratör üzerinden e\-Fatura/e\-Arşiv (dinamik seçilebilir) — bkz. §10 |

### 4\.1 Dinamik Sağlayıcı Yönetimi (Provider Registry)

Ödeme, kargo, pazaryeri ve e\-Fatura entegrasyonlarının tümü **aynı mimari kalıp** üzerinden, kod değişikliği/yeniden deploy gerektirmeden admin panelden **dinamik olarak** açılıp kapatılabilecek şekilde tasarlanır. Bu, §9 ve §10'da ayrı ayrı bahsedilen "admin panelden aktif/pasif etme" ihtiyacını tek bir ortak, tutarlı mekanizmaya bağlar:

- Her sağlayıcı tipi için ortak bir sözleşme tanımlanır: `IPaymentGateway`, `ICargoProvider`, `IMarketplaceConnector`, `IEInvoiceProvider`. Her biri, kendi çalışması için gereken alanları (`ConfigFieldDefinition`\: anahtar, etiket, tip — metin/şifre/seçim listesi —, zorunlu mu) **kod içinde deklare eder**; ör. Trendyol connector'ı `SupplierId`, `ApiKey`, `ApiSecret` alanlarını ister, DHL connector'ı `AccountNumber`, `ApiKey` ister.
- Kod tarafında, MVP'de **kullanılabilecek tüm connector sınıfları** (iyzico, PayTR, Param, PayPal, Stripe / Yurtiçi, Aras, MNG, DHL, UPS, FedEx / Trendyol, Hepsiburada, N11, İdefix, Amazon / BizimHesap, Nilvera, Uyumsoft, Foriba, İzibiz) yazılıp bir `ProviderRegistry`'e kaydedilir — ancak **hiçbiri varsayılan olarak aktif değildir**.
- Admin panelde jenerik bir **"Entegrasyonlar"** ekranı bulunur: Ödeme / Kargo / Pazaryeri / e\-Fatura sekmeleri altında kayıtlı tüm connector'lar birer kart olarak listelenir (durum: *Yapılandırılmadı* / *Aktif* / *Hata*). Bir karta tıklandığında, o connector'ın deklare ettiği alanlara göre **otomatik/metadata\-güdümlü bir form** render edilir (Blazor'da dinamik `RenderFragment` veya component\-per\-field\-type ile). Kullanıcı bilgileri girer, sistem bir bağlantı testi (health check) dener, başarılıysa sağlayıcı **Aktif** duruma geçer ve o andan itibaren ilgili akışlarda (checkout, kargo hesaplama, pazaryeri senkronu, fatura kesme) kullanılabilir hale gelir.
- Bu yaklaşımın somut faydası: **hiçbir sağlayıcı için "ilk sürümde mutlaka kod yazıp devreye almak" zorunlu değildir** — geliştirme ekibi tüm connector'ları yazıp teslim eder, hangisinin/hangilerinin ne zaman kullanılacağına firma kendi operasyonel temposuna göre admin panelden karar verir (ör. Trendyol bilgileri elde eder etmez o gün aktifleştirir; İdefix ile henüz anlaşma yoksa kartı boş/pasif kalır).
- Sağlayıcı anahtarları (API key/secret) her zaman Data Protection API ile şifreli saklanır; hangi kullanıcının hangi sağlayıcıyı ne zaman aktif/pasif ettiği `AuditLog`'a yazılır.

**Önerilen çözüm (solution) klasör yapısı:**

```text
Dekorras/                                 (monorepo kök dizini)
├── backend/
│   ├── Dekorras.sln
│   ├── src/
│   │   ├── Core/
│   │   │   ├── Dekorras.Domain/              → Entities (Katalog, Sipariş, Muhasebe/Cari/
│   │   │   │                                    Fatura/Kasa dahil), ValueObjects, Enums,
│   │   │   │                                    Events, Exceptions
│   │   │   └── Dekorras.Application/         → CQRS Handlers (ECommerce + Accounting modülleri),
│   │   │                                       DTOs, Validators, Interfaces (Provider Registry
│   │   │                                       sözleşmeleri dahil), Mapping
│   │   ├── Infrastructure/
│   │   │   ├── Dekorras.Infrastructure/      → ProviderRegistry, Storage, Cache, E-posta
│   │   │   │   ├── PaymentProviders/         → iyzico, PayTR, Param, PayPal, Stripe adaptörleri
│   │   │   │   ├── CargoProviders/           → Yurtiçi, Aras, MNG, DHL, UPS, FedEx adaptörleri
│   │   │   │   ├── MarketplaceConnectors/    → Trendyol, Hepsiburada, N11, İdefix, Amazon adaptörleri
│   │   │   │   └── EInvoiceProviders/        → BizimHesap, Nilvera, Uyumsoft, Foriba, İzibiz adaptörleri
│   │   │   └── Dekorras.Persistence/         → SQL Server DbContext, EF Core Configurations, Migrations
│   │   └── Presentation/
│   │       ├── Dekorras.Api/                 → REST API — Admin (her iki modül) + Storefront +
│   │       │                                    Flutter mobil ortak backend
│   │       ├── Dekorras.Admin/               → Yönetim paneli (Blazor Server)
│   │       │   ├── Pages/ModuleSelector/     → Giriş sonrası Muhasebe/E-Ticaret seçim ekranı
│   │       │   ├── Pages/Integrations/       → Dinamik "Entegrasyonlar" ekranı (§4.1)
│   │       │   ├── Areas/ECommerce/          → Katalog, sipariş, kampanya, pazaryeri ekranları
│   │       │   └── Areas/Accounting/         → Cari, kasa/banka, fatura/irsaliye, teklif ekranları
│   │       ├── Dekorras.Storefront/          → Mağaza ön yüzü (SSR — MVC/Razor Pages), TR/EN/DE/
│   │       │                                    FR/NL/ES/AR (RTL) dil desteği
│   │       └── Dekorras.MarketplaceSync/     → (opsiyonel) Pazaryeri senkron arka plan servisi (Worker Service)
│   └── tests/
│       ├── Dekorras.Domain.Tests/
│       ├── Dekorras.Application.Tests/
│       └── Dekorras.IntegrationTests/        → Testcontainers ile SQL Server üzerinde uçtan uca testler
└── mobile/
    └── dekorras_app/                         → Flutter (Dart) mobil uygulaması — Dekorras.Api tüketicisi
```

## 5\. Teknoloji Yığını Önerisi

| Alan | Öneri |
| --- | --- |
| Çerçeve | .NET 9 / ASP.NET Core 9 |
| Mimari desen | Onion Architecture \+ CQRS (MediatR) \+ Provider Registry (§4.1) |
| ORM | Entity Framework Core 9 (Code\-First, Migrations) |
| Veritabanı | **Microsoft SQL Server** (kesinleşti) |
| Doğrulama | FluentValidation |
| Mapping | Mapster veya AutoMapper |
| Kimlik & Yetkilendirme | ASP.NET Core Identity \+ JWT (API/mobil) \+ Refresh Token \+ rol/modül bazlı yetkilendirme (RBAC — Muhasebe/E\-Ticaret modül erişimi dahil) |
| Loglama | Serilog \+ Seq / ELK |
| İzleme | OpenTelemetry / Application Insights |
| Önbellek | Redis (sepet oturumu, sık erişilen katalog verisi, dağıtık kilit) |
| Arka plan görevleri | Hangfire (kampanya bitişi, stok senkronizasyonu, e\-posta kuyruğu, döviz kuru güncelleme, pazaryeri senkron job'ları, e\-Fatura durum sorgulama, terk edilmiş sepet hatırlatması) |
| Arama | Elasticsearch veya Meilisearch (3.100\+ ürün için facet'li/filtreli arama) |
| Ödeme | iyzico, PayTR, Param, firmanın kendi banka/POS hesapları \+ uluslararası PayPal/Stripe — **hepsi dinamik giriş ekranlarıyla admin panelden isteğe bağlı aktifleştirilir** (§4.1); checkout'ta son seçim müşteriye aittir |
| Kargo | Yurtiçi Kargo / Aras Kargo / MNG Kargo (yurt içi) \+ DHL / UPS / FedEx (yurt dışı) — **her biri admin panelden dinamik olarak, hangisiyle sözleşme yapıldıysa o aktifleştirilir** |
| Pazaryeri entegrasyonu | Trendyol Marketplace API, Hepsiburada Merchant API, N11 API, İdefix Satıcı API, Amazon SP\-API — **her biri isteğe bağlı, modül ekranında bilgi girilerek dinamik aktifleştirilir** (§9, §4.1) |
| Muhasebe / e\-Fatura | Native Cari/Kasa/Fatura modeli \+ sertifikalı bir e\-Fatura/e\-Arşiv entegratörü — BizimHesap'ın kendi API'si (apidocs.bizimhesap.com, §2.8) veya Nilvera/Uyumsoft/Foriba/İzibiz — **hangisi seçilirse admin panelden dinamik olarak aktifleştirilir** (§10.3, §4.1) |
| Dosya/Görsel depolama | Azure Blob Storage veya S3 uyumlu (MinIO) \+ CDN |
| Çoklu dil/para birimi | ASP.NET Core Localization (`.resx` veya DB\-tabanlı çeviri) — TR, EN, DE, FR, NL, ES, AR (AR için RTL desteği); güncel döviz kuru servisi entegrasyonu (TCMB/üçüncü parti API) |
| Mobil | **Flutter (Dart)** — Riverpod veya BLoC state management, Dio HTTP client, Firebase Cloud Messaging (push bildirim), flutter\_secure\_storage (token saklama), Flutter'ın yerleşik `Directionality`/RTL desteği (Arapça için) |
| Konteynerleştirme | Docker \+ Docker Compose |
| CI/CD | GitHub Actions veya Azure DevOps Pipelines — backend ve Flutter için ayrı pipeline'lar |
| Test | xUnit, FluentAssertions, NSubstitute, Testcontainers, Bogus (backend); flutter\_test \+ integration\_test (mobil) |

## 6\. Domain Modülleri ve Ana Varlıklar

| Modül (Bounded Context) | Sorumluluk | Ana Entity'ler |
| --- | --- | --- |
| Katalog | Kategori ağacı, ürün, marka, varyant, görsel yönetimi | `Category` (self\-referencing), `Product` (birim/UoM alanı dahil — adet/m²/mt), `ProductVariant`, `ProductAttribute`, `Brand`, `ProductImage`, `ProductVideo`, `QuantityDiscount`, `ProductReview`, `ProductQuestion` |
| Sipariş & Sepet | Sepet, sipariş, durum makinesi, kupon | `Cart`, `CartItem`, `Order` (kaynağı `OrderSource` ile işaretli: Web/Mobil/Trendyol/Hepsiburada/…), `OrderItem`, `OrderStatusHistory`, `Coupon`, `GiftVoucher` |
| Müşteri | Bireysel/Kurumsal müşteri, adres, ortaklık, favoriler | `Customer`, `CustomerGroup`, `Address` (uluslararası formatı destekler), `Affiliate`, `WishlistItem` |
| Entegrasyon / Provider Registry | Ödeme, kargo, pazaryeri, e\-Fatura sağlayıcılarının dinamik yönetimi (§4.1) | `IntegrationProvider` (tip: Payment/Cargo/Marketplace/EInvoice, durum, şifreli config), `IntegrationConfigField`, `IntegrationHealthCheckLog` |
| Ödeme | Çoklu sağlayıcılı ödeme işlemleri | `Payment`, `PaymentProvider` (Entegrasyon modülündeki `IntegrationProvider` kaydına bağlı), `Transaction` |
| Kargo | Kargo yöntemi, ücretlendirme, takip (yurt içi \+ yurt dışı) | `ShippingMethod`, `ShippingRate`, `ShipmentTracking`, `CustomsDeclaration` (e\-ihracat) |
| Pazaryeri Entegrasyonları | Trendyol/Hepsiburada/N11/İdefix/Amazon ile çift yönlü senkron (bkz. §9) | `MarketplaceAccount`, `MarketplaceListing`, `MarketplaceCategoryMapping`, `MarketplaceOrder`, `MarketplaceSyncLog` |
| **Muhasebe** | Cari hesap, kasa/banka, fatura/irsaliye, teklif, e\-Fatura (bkz. §10) | `LedgerAccount` (Cari — müşteri/tedarikçi ortak), `LedgerTransaction`, `Invoice`, `Waybill`, `Quote`, `CashRegister`, `BankAccount`, `Check`, `PromissoryNote`, `Expense`, `EInvoiceLog` |
| Lokalizasyon | Çok dil / çok para birimi | `Language` (TR/EN/DE/FR/NL/ES/AR, `IsRightToLeft` bayrağı), `Translation`, `Currency`, `ExchangeRate` |
| İçerik/CMS | Statik sayfalar, blog, banner | `CmsPage`, `BlogPost`, `Banner`, `MenuItem` |
| Pazarlama | Kampanya motoru, bülten | `Campaign`, `PromotionRule`, `NewsletterSubscriber` |
| Sistem/Yönetim | Kullanıcı, rol, izin, ayar, denetim kaydı, **modül erişimi** | `AdminUser`, `Role`, `Permission` (Muhasebe/E\-Ticaret modül izinleri dahil), `Setting`, `AuditLog` |
| Bildirim | E\-posta/SMS/push şablonları ve gönderim kaydı | `EmailTemplate`, `PushNotification`, `NotificationLog` |

*Not: "Pazaryeri" (çoklu satıcı) modülü — yani Dekorras'ın kendisinin bir marketplace platformuna dönüşmesi — mevcut sistemde hazır fakat kapalı görünüyor; ilk sürümde kapsam dışı bırakılıp yol haritasında opsiyonel bir faz olarak planlanması önerilir. Bu, §9'daki "hazır pazaryerlerinde satıcı olarak yer alma" konusundan tamamen farklı bir modüldür.*

## 7\. E\-İhracat (Sınır Ötesi Satış) Uyumluluğu

Sistemin yurt dışı satışa da açık olması istendiği için Domain ve Application katmanları en baştan aşağıdaki gereksinimlere göre tasarlanmalıdır — bunları sonradan eklemek çok daha maliyetlidir:

- **Çok dilli içerik modeli:** Hedef diller **Türkçe, İngilizce, Almanca, Fransızca, Hollandaca, İspanyolca ve Arapça** olarak belirlenmiştir. `Product`, `Category`, `CmsPage` gibi tüm müşteriye görünen içerikler dil bazlı (`ProductTranslation` vb.) tutulmalı; yapı yeni dillerin eklenmesine kapalı olmamalı.
- **Arapça için sağdan\-sola (RTL) düzen:** Arapça, projedeki tek RTL dildir ve ayrı bir mühendislik kalemi olarak planlanmalıdır — Storefront'ta CSS mantıksal özellikler (`margin-inline-start` vb.) ve `dir="rtl"` desteği, Flutter tarafında `Directionality`/`Bidi` desteği gerekir. Admin panelin (Blazor) kendisi TR kalabilir; RTL yalnızca müşteri yüzü (storefront \+ mobil) için gereklidir.
- **Çok para birimli fiyatlandırma:** Ürün fiyatları temel para biriminde (TRY) tutulur; storefront ve mobil uygulama, güncel kur (TCMB veya üçüncü parti kur API'si, Hangfire ile periyodik güncellenir) üzerinden müşterinin seçtiği para biriminde gösterim yapar.
- **Esnek adres şeması:** `Address` entity'si ülkeye göre değişen posta kodu/eyalet\-il alanlarını destekleyecek şekilde tasarlanmalı (zorunlu alanlar ülkeye göre dinamik).
- **Gümrük ve fatura gereksinimleri:** Ürünlerde HS/GTİP kodu alanı, proforma fatura üretimi, yurt dışı satışlarda KDV istisnası (%0 KDV) hesaplama mantığı — bu, §10'daki Muhasebe modülünün fatura kesme akışıyla doğrudan bağlantılıdır.
- **Uluslararası kargo:** DHL, UPS, FedEx — hangisi/hangileriyle sözleşme yapılırsa admin panelden dinamik olarak aktifleştirilir (§4.1); `ICargoProvider` soyutlaması yurt dışı gönderi/gümrük beyanı senaryolarını da kapsar.
- **Uluslararası ödeme:** iyzico/PayTR/Param'ın yurt dışı kartları da desteklemesi \+ gerekirse PayPal/Stripe gibi global sağlayıcıların aynı Provider Registry'ye dinamik olarak eklenmesi.
- **Yasal:** KVKK'ya ek olarak, AB'ye satış öngörülüyorsa (Almanca/Fransızca/Hollandaca/İspanyolca pazarları AB üyesi ülkeleri işaret ediyor) **GDPR** uyumluluğu (açık rıza, veri saklama/silme talepleri) değerlendirilmelidir.
- **Uluslararası pazaryeri kanalları:** Amazon, Etsy, AliExpress, Ozon gibi kanallar (BizimHesap'ın E\-Ticaret modülünde de "E\-ihracat" kategorisi altında zaten listeleniyor — bkz. §2.8) hem yurt içi hem yurt dışı satış kanalı olarak kullanılabilir; bu kanal, §9'daki aynı `IMarketplaceConnector` alt yapısı üzerinden yönetilir.

## 8\. Mobil Uygulama (Flutter)

Mobil erişim iki parçadan oluşur: **(1)** `Dekorras.Api` — storefront, admin panel ve mobil uygulamanın ortak tükettiği tek REST API; **(2)** bu API'yi tüketen, ayrı bir proje olarak geliştirilecek **Flutter tabanlı mobil uygulama**.

**Mimari yaklaşım:** Flutter tarafında da Onion'ın ruhuna uygun bir katmanlama önerilir — `presentation` (widget'lar, sayfalar), `domain` (iş modelleri, use case'ler), `data` (API istemcisi, repository implementasyonları) — böylece backend ile aynı "iş kuralları merkezi, altyapı dıştan değiştirilebilir" prensibi mobilde de korunur.

**Kapsanması gereken temel akışlar:**

- Kategori/ürün listeleme ve detay (storefront ile aynı katalog verisi, aynı API)
- Sepet ve checkout — **aktif ödeme sağlayıcıları arasından seçim** (§4.1'deki Provider Registry'den dönen aktif liste)
- Üyelik: kayıt, JWT ile giriş, misafir alışverişi, sipariş geçmişi, favoriler
- Sipariş takibi ve push bildirimleri (Firebase Cloud Messaging — sipariş durumu değişince, kampanya duyurularında)
- Çok dil (TR/EN/DE/FR/NL/ES/AR, Arapça için RTL) / çok para birimi desteği (§7 ile aynı alt yapı)
- Güvenli token saklama (`flutter_secure_storage`), biyometrik giriş (opsiyonel iyileştirme)

**Teslimat:** `mobile/dekorras_app` klasöründe bağımsız bir Flutter projesi; CI pipeline'ı backend'den ayrı ama aynı monorepo içinde yönetilir. iOS/Android mağaza yayını için gereken hazırlıklar §16'da ayrı bir kontrol listesi olarak ele alınmıştır.

## 9\. Ticari Pazaryeri Entegrasyonları (Trendyol, Hepsiburada, N11, İdefix, Amazon vb.)

Dekorras'ın kendi web/mobil kanallarının yanı sıra, Türkiye'nin başlıca hazır e\-ticaret pazaryerlerinde de **satıcı olarak** yer alması ve tüm kanalların **tek bir merkezi admin panelden** yönetilmesi (omnichannel/çoklu kanal satış yönetimi) hedeflenmektedir. Bu yaklaşımın firmanın halihazırda kullandığı BizimHesap'ın kendi E\-Ticaret modülünde de (§2.8) aynı mantıkla (ürün eşleştirme, listeleme, fiyat güncelleme) sunulduğu görülmüştür — yani firma için tanıdık ve kanıtlanmış bir iş modelidir.

**Kavramsal ayrım:** §2.3'te bahsedilen mevcut sistemdeki "Pazaryeri" modülü, Dekorras'ın KENDİSİNİ bir pazaryerine (çoklu satıcılı platform) dönüştürmesi içindir. Bu bölümdeki entegrasyon ise **tam tersi yöndedir**\: Dekorras, kendi ürünlerini Trendyol, Hepsiburada, N11, İdefix, Amazon gibi HAZIR pazaryerlerinde SATICI olarak listeler ve bu kanalları merkezi sistemden yönetir.

### 9\.1 Mimari Yaklaşım

Ödeme ve kargo entegrasyonlarında olduğu gibi, her pazaryeri için ayrı bir adaptör yazılır; Application katmanında tanımlanan tek bir `IMarketplaceConnector` interface'i üzerinden, §4.1'deki Provider Registry mekanizmasıyla çalışırlar. Bu sayede yeni bir pazaryeri eklemek, mevcut kodu değiştirmeden yeni bir Infrastructure implementasyonu eklemek kadar basit olur (Open/Closed Principle).

`IMarketplaceConnector` arayüzünün karşılaması gereken temel sorumluluklar:

| Sorumluluk | Açıklama |
| --- | --- |
| Ürün gönderimi (listeleme) | Kendi kataloğundaki ürünü pazaryerinin kategori/özellik şablonuna eşleyip (bkz. `MarketplaceCategoryMapping`) o pazaryerinde yayına alma |
| Stok senkronizasyonu | Merkezi stok (tek doğruluk kaynağı) her değiştiğinde tüm aktif pazaryerlerine anlık veya kısa aralıklarla push edilmesi |
| Fiyat senkronizasyonu | Pazaryeri komisyon oranına göre kanal bazlı satış fiyatı hesaplanıp gönderilmesi |
| Sipariş çekme | Pazaryerinden gelen siparişlerin merkezi `Order` modeline `MarketplaceOrder` olarak aktarılması — tüm siparişler (web, mobil, Trendyol, Hepsiburada…) admin panelde **tek bir Satış ekranında** birleşir |
| Sipariş/kargo durumu bildirimi | Merkezi sistemde kargoya verilen bir siparişin takip numarasının ilgili pazaryerine geri bildirilmesi |
| İade/iptal senkronizasyonu | Pazaryerinden gelen iade/iptal taleplerinin merkezi sipariş durum makinesine yansıtılması |

### 9\.2 Kritik Tasarım Noktaları

- **Kategori ve özellik eşleme:** Her pazaryerinin kendi kategori ağacı ve zorunlu ürün özellikleri (ör. Trendyol'da barkod/GTIN zorunluluğu) vardır; admin panelde kendi kategorilerinizi her pazaryerinin kategorisine eşleyeceğiniz bir ekran (`MarketplaceCategoryMapping` yönetimi) gerekir.
- **Senkronizasyon sıklığı:** Kritik değişiklikler (stok tükenmesi, fiyat değişimi) domain event'leri ile (`StockChanged`, `PriceChanged`) anlık tetiklenmeli; toplu/rutin senkron ise Hangfire ile periyodik job olarak çalışmalı.
- **Webhook desteği:** Trendyol ve Hepsiburada gibi webhook sağlayan pazaryerlerinden gelen sipariş/iade bildirimleri için API'de ayrı webhook alma uç noktaları (endpoint) tanımlanmalı.
- **Hata yönetimi ve izlenebilirlik:** Her senkron denemesi `MarketplaceSyncLog`'a kaydedilmeli; başarısız denemeler için retry/backoff mekanizması ve admin panelde bir "senkron hataları" bildirim ekranı bulunmalı.
- **API kısıtları:** Her pazaryerinin kendi rate limit ve kimlik doğrulama şeması (API key/secret veya OAuth) vardır; bu bilgiler §4.1'deki Provider Registry ekranı üzerinden güvenli şekilde (şifreli) girilip yönetilmelidir.
- **Ölçeklenebilirlik:** Yük arttıkça senkron işleri, API'nin isteğe\-yanıt döngüsünü yormaması için ayrı bir arka plan servisine (`Dekorras.MarketplaceSync`, bağımsız bir Worker Service host'u) taşınabilir; bu, Onion mimaride yalnızca yeni bir Presentation/Host eklemek anlamına gelir, Domain/Application katmanları değişmez.

**Kapsam ve önceliklendirme:** Beş pazaryerinin (Trendyol, Hepsiburada, N11, İdefix, Amazon) tümü için `IMarketplaceConnector` implementasyonu Faz 7'de (§13) yazılır ve teslim edilir; **hiçbiri MVP çıkışında zorunlu olarak aktif değildir**. Hangi pazaryeri/pazaryerlerinin gerçekten kullanılacağı, firmanın o kanalla satıcı hesabı/API anlaşması olup olmadığına bağlı olarak, admin panelin Entegrasyonlar \> Pazaryeri sekmesinden **isteğe bağlı ve dinamik olarak** — kod değişikliği gerekmeden — aktifleştirilir (bkz. §4.1).

## 10\. Muhasebe Modülü ve Modül Seçim Ekranı

§2.8'deki BizimHesap analizine dayanarak, sisteme **native bir Muhasebe modülü** eklenmesi ve admin panele girişte kullanıcının **Muhasebe** veya **E\-Ticaret** modülünden birini seçebileceği bir ekran sunulması hedeflenmektedir.

### 10\.1 Modül Seçim Ekranı

Blazor Admin uygulamasına girişte (kimlik doğrulamadan hemen sonra), kullanıcıyı iki büyük seçenek kartıyla karşılayan bir **`ModuleSelector`** sayfası eklenir:

- **\[ Muhasebe Modülü \]** — Cari hesaplar, kasa/banka, fatura/irsaliye, teklif, gider, raporlar
- **\[ E\-Ticaret Modülü \]** — Katalog, siparişler, kampanyalar, pazaryeri entegrasyonları, müşteriler

Tasarım kuralları:

- Bu ekran, kullanıcının `Permission` kayıtlarına göre **RBAC ile filtrelenir**\: yalnızca Muhasebe yetkisi olan bir kullanıcı yalnızca o kartı görür (ve otomatik o modüle yönlenebilir); her iki yetkiye de sahip bir yönetici (ör. Turan Yılmaz) her iki kartı da görüp istediğini seçer.
- Seçim sonrası kullanıcı ilgili modülün kendi menü/layout'una geçer (`Areas/Accounting` veya `Areas/ECommerce`); üst köşede her zaman "Modül Değiştir" bağlantısı bulunur, böylece iki modül arasında sayfa yenilemeden geçiş yapılabilir.
- Varsayılan olarak son seçilen modül hatırlanır (kullanıcı bazlı tercih), ancak "Modül Değiştir" her zaman erişilebilir kalır.

### 10\.2 Muhasebe Modülü Kapsamı (BizimHesap'tan Esinlenilen Yapı)

| Ekran Grubu | İçerik (BizimHesap analizinden uyarlanmıştır) |
| --- | --- |
| Muhasebe Dashboard | Bugünkü satış/tahsilat, dönemsel ciro/masraf, stok değeri; Varlıklar (Kasa/POS/Banka/Çek/Senet/Stok/Açık Hesap) ve Borçlar (Açık Hesap/Senet/Kredi Kartı) özet kartları; yaklaşan çek/senet ve masraf takvimi |
| Cari Hesaplar | Müşteri ve tedarikçi ortak cari hesap listesi (`LedgerAccount`), açık bakiye, çek/senet bakiyesi, hareket dökümü; toplu Excel içe/dışa aktarım |
| Ürün/Stok (Muhasebe görünümü) | E\-Ticaret modülüyle **aynı** `Product` verisini kullanır — ayrı bir stok kaydı tutulmaz; birim (adet/m²/mt) bazlı stok değerlemesi |
| Satışlar / Alışlar | Perakende / kayıtlı müşteriyle hızlı satış girişi; belge durumu **Taslak → İrsaliyeleşmiş → Faturalaşmış (E\-Fatura/E\-Arşiv)** akışı; her satış, ilişkili olduğu e\-ticaret/pazaryeri siparişine `OrderId` ile bağlanır |
| Teklifler | Teklif oluşturma, PDF/şablon çıktısı, tekliften siparişe dönüştürme |
| Nakit Yönetimi | Kasa, Banka, Çek, Senet hareketleri; vade takibi |
| Giderler | Masraf kaydı, kategori bazlı gider takibi |
| e\-Fatura / e\-Arşiv | Resmi belge kesimi ve GİB'e iletim — bkz. §10.3, dinamik entegratör seçimi §4.1 |
| Raporlar | Cari ekstre, kâr/zarar, stok değer raporu, vade/yaşlandırma raporu |

**Sipariş → Fatura otomasyonu:** E\-Ticaret modülünde bir sipariş "Tamamlandı" durumuna geçtiğinde, Muhasebe modülünde otomatik olarak ilgili cari hesaba bağlı bir fatura/irsaliye taslağı (`Invoice`/`Waybill`, `OrderId` referanslı) oluşturulur — böylece BizimHesap'ta gözlemlenen "Sipariş No" sütununun manuel eşleştirilmesi ihtiyacı ortadan kalkar; kullanıcı yalnızca taslağı onaylayıp e\-Fatura/e\-Arşiv olarak keser.

### 10\.3 e\-Fatura/e\-Arşiv İçin Mimari Karar

Resmi **e\-Fatura/e\-Arşiv** gönderimi, GİB (Gelir İdaresi Başkanlığı) nezdinde **"özel entegratör"** sertifikasyonu gerektiren, mali mühür ve teminat mektubu gibi ağır regülasyon yükümlülükleri taşıyan bir alandır. Bunu sıfırdan kurmak (kendi özel entegratörlüğünüzü almak) proje kapsamının çok ötesindedir. Bu nedenle:

- Muhasebe modülünün **Cari, Kasa/Banka, Teklif, Gider, Stok değerleme** gibi tüm iş mantığı **native olarak** Domain/Application katmanında inşa edilir (BizimHesap'ın UX'inden esinlenerek).
- Belgenin **resmi e\-Fatura/e\-Arşiv olarak GİB'e iletilmesi** kısmı, `IEInvoiceProvider` soyutlaması üzerinden §4.1'deki Provider Registry'ye kayıtlı, **sertifikalı bir entegratörün API'sine** devredilir.
- **Aday 1 — BizimHesap'ın kendi API'si:** `apidocs.bizimhesap.com` üzerinde Sipariş/Fatura Ekleme, Ürün Listesi, Depo/Stok uç noktalarını kapsayan genel bir entegrasyon API'si yayınlandığı doğrulanmıştır (bkz. §2.8). Bu, mevcut BizimHesap hesabınızın en azından temel senkron için uygun bir aday olduğunu gösteriyor; ancak API anahtarı edinme koşulları, plan/paket kısıtları ve resmi e\-Fatura/e\-Arşiv'in bizzat bu API üzerinden mi kesildiği **BizimHesap destek/satış ekibiyle teyit edilmelidir** (bkz. §15).
- **Aday 2 — bağımsız entegratörler:** Nilvera, Uyumsoft, Foriba, İzibiz gibi GİB sertifikalı özel entegratörler.
- Hangi aday seçilirse seçilsin, admin panelde Entegrasyonlar \> e\-Fatura sekmesinde **birden fazla entegratör kartı** hazır bulunur (BizimHesap, Nilvera, Uyumsoft, Foriba, İzibiz); firma hangisiyle anlaşırsa o kartın API kimlik bilgilerini girip aktifleştirir — kod değişikliği gerekmez. Bu, ödeme ve kargo entegrasyonlarında kullanılan Strategy Pattern ile birebir aynı prensiptir.
- Bu karar, projenin **hukuki/regülasyon riskini** ortadan kaldırırken BizimHesap'ın sağladığı kullanım kolaylığını native bir modül olarak sisteme taşımayı mümkün kılar.

## 11\. Güvenlik, Uyumluluk ve API Tasarımı

- **KVKK** (6698 sayılı Kişisel Verilerin Korunması Kanunu) ve **Mesafeli Satış Yönetmeliği** uyumu — mevcut yasal metinler CMS modülüne taşınmalı, açık rıza akışları korunmalı; yurt dışı satışta (özellikle AB pazarları — Almanca/Fransızca/Hollandaca/İspanyolca dilleri) **GDPR** de değerlendirilmeli (bkz. §7).
- REST API, `api/v1` versiyonlama, OpenAPI/Swagger dokümantasyonu, RFC 7807 (Problem Details) standart hata formatı — **storefront, admin panel (her iki modül), Flutter mobil uygulama ve pazaryeri senkron servisinin ortak tükettiği tek API**.
- Kimlik doğrulama: Identity \+ JWT (storefront/API/mobil), admin panelde Blazor Server'ın oturum tabanlı kimlik doğrulaması \+ 2FA (mevcut sistemde olmayan, önerilen bir iyileştirme).
- Rol ve **modül** bazlı yetkilendirme (RBAC): Yönetici (her iki modül), Muhasebeci (yalnızca Muhasebe), Editör/Müşteri Hizmetleri (yalnızca E\-Ticaret), Bireysel Müşteri, Kurumsal Müşteri.
- reCAPTCHA v3, rate limiting, güvenlik başlıkları (CSP, HSTS), EF Core parametrik sorgularla SQL Injection koruması.
- Ödeme, kargo, pazaryeri ve e\-Fatura entegratör anahtarları (API key/secret) §4.1'deki Provider Registry üzerinden yönetilir ama Data Protection API ile şifreli saklanır; kart verisi asla kendi sunucularında tutulmaz (PCI\-DSS kapsamı dışında kalmak için sağlayıcıların hosted/iframe çözümleri kullanılır); mali/muhasebe verisine erişim ayrıca denetim kaydına (`AuditLog`) bağlanmalıdır.

## 12\. Veri Taşıma (Migrasyon) Stratejisi

1. Mevcut admin panelden ürün/kategori/marka/müşteri verilerinin toplu export'u (CSV/Excel veya doğrudan MariaDB dökümü — barındırma sağlayıcısından erişim istenmeli).
2. \~3.100 ürüne ait görsellerin toplu indirilip yeni depolama sistemine (Blob/S3) aktarılması.
3. **Mevcut SEO URL yapısının (slug) birebir korunması** — arama motoru sıralaması kaybını önlemek için eski→yeni URL eşleme tablosu ve 301 yönlendirmeleri.
4. Sipariş geçmişi (düşük hacim, 9 kayıt) ve 110 müşteri hesabının taşınması; müşteriler için zorunlu şifre sıfırlama akışı.
5. Ödeme sağlayıcılarının (iyzico/PayTR/Param \+ firmanın kendi hesapları) yeniden sözleşme/API anahtarı tanımlama süreci — eski sistemdeki saklı kart/token verisi taşınamaz, müşteriler yeniden kart girer.
6. Varsa mevcut pazaryeri (Trendyol/Hepsiburada vb.) mağaza hesaplarının API kimlik bilgilerinin yeni sisteme aktarılması ve ürün eşlemelerinin (kendi SKU ↔ pazaryeri ürün ID'si) yeniden kurulması.
7. **BizimHesap'tan muhasebe verisinin taşınması** — 389 cari hesap (bakiyeleri dahil), ürün/stok kartları (birim bilgisiyle), açık/geçmiş fatura ve irsaliye kayıtları, çek/senet portföyü Excel export/import veya BizimHesap API'si üzerinden aktarılmalı; e\-Fatura geçmişi (mali/yasal saklama zorunluluğu nedeniyle) BizimHesap'ta arşiv olarak bir süre daha erişilebilir tutulmalı.
8. Kademeli/paralel geçiş: yeni sistem (web \+ mobil \+ pazaryeri senkronu \+ muhasebe) hazırlanırken eski site ve BizimHesap canlı kalır; DNS ve yönlendirmelerle anahtar teslim geçiş.

## 13\. Yol Haritası

| Faz | Kapsam | Tahmini Süre |
| --- | --- | --- |
| 0 | Keşif & teknik tasarım — DB şeması, API sözleşmesi, UI/UX onayı | 1\-2 hafta |
| 1 | Onion katmanları, kimlik doğrulama, temel altyapı (SQL Server \+ EF Core), Provider Registry çekirdeği (§4.1) | 2\-3 hafta |
| 2 | Katalog yönetimi \+ Blazor admin panel CRUD ekranları | 3\-4 hafta |
| 3 | SSR Storefront — mağaza, sepet, arama/filtreleme, SEO, çok dil (TR/EN/DE/FR/NL/ES/AR) ve çok para birimi alt yapısı | 3\-4 hafta |
| 4 | Checkout, ödeme sağlayıcı connector'ları (Provider Registry'ye kayıt), kargo connector'ları (yurt içi \+ DHL/UPS/FedEx), sipariş durum makinesi | 3\-4 hafta |
| 5 | Kampanya motoru, bülten, blog, (opsiyonel) ortaklık sistemi | 2 hafta |
| 6 | E\-ihracat detayları — Arapça RTL desteği, gümrük/fatura akışı, GDPR gözden geçirme, uluslararası kargo testleri | 1\-2 hafta |
| 7 | Pazaryeri connector'ları (Trendyol, Hepsiburada, N11, İdefix, Amazon) — beşi de yazılır, hangisinin aktifleştirileceği firmaya bırakılır | 3\-4 hafta |
| 8 | **Muhasebe modülü** — modül seçim ekranı, cari/kasa/fatura/teklif ekranları, e\-Fatura entegratör connector'ları (BizimHesap/Nilvera/Uyumsoft/Foriba/İzibiz), sipariş→fatura otomasyonu | 3\-4 hafta |
| 9 | Flutter mobil uygulama — API tüketimi, checkout, push bildirim | 3\-4 hafta |
| 10 | Mobil mağaza yayın hazırlığı (§16) ve store'a gönderim | 1\-2 hafta (hesap onay sürelerine bağlı) |
| 11 | Veri taşıma (e\-ticaret \+ BizimHesap muhasebe verisi), UAT, performans testleri, kesintisiz go\-live | 2\-3 hafta |

## 14\. Kapsamlı Geliştirme Promptu

Aşağıdaki blok, bir yazılım ekibine veya bir AI kodlama asistanına **doğrudan kopyalanıp verilebilecek** şekilde hazırlanmıştır. Tek bir parça halinde tasarlanmıştır; kopyalarken tamamını alın.

```markdown
ROL
Sen kıdemli bir .NET mimarı, full-stack yazılımcı ve Flutter mobil geliştiricisisin.
Aşağıdaki gereksinimlere göre sıfırdan, production-ready bir iş sistemi (web mağazası +
yönetim paneli + muhasebe modülü + mobil uygulama + pazaryeri entegrasyonları)
tasarlayıp kodlayacaksın.

BAĞLAM — MEVCUT DURUM
Dekorras, Kayseri merkezli iç/dış cephe dekorasyon malzemeleri (söve, duvar kaplaması,
duvar kağıdı, taş desen panel, kartonpiyer, poster vb.) satan bir üretici/e-ticaret
firmasıdır. Şu an dekorras.com adresinde, PHP 7.3 + MariaDB tabanlı, IonCube ile
şifrelenmiş kapalı kaynaklı bir SaaS platformu (KobiDirekt, OpenCart çekirdekli,
Journal 3 teması) üzerinde çalışıyor. Ayrıca firma, cari hesap/stok/fatura/e-Fatura
işlemlerini BizimHesap adlı ayrı bir ön muhasebe SaaS'ı üzerinden yürütüyor (389 cari
hesap, yoğun günlük satış/fatura trafiği, apidocs.bizimhesap.com adresinde yayınlanmış
bir entegrasyon API'si mevcut). Bu iki sistemi TEK bir platformda birleştirip kendi kod
tabanımıza sahip olmak istiyoruz.

Mevcut e-ticaret sisteminde tespit edilen ölçek ve iş kuralları:
- ~3.100 ürün, 103 kategori (3 seviyeye kadar iç içe), 8 marka
- 110 müşteri, iki müşteri grubu: Bireysel ve Kurumsal (B2C/B2B fiyat farkı var)
- Ürünlerde minimum satış adedi kısıtı var (toptan satış mantığı)
- 14 farklı sipariş durumu (Onay Bekliyor, Hazırlanıyor, Hazırlandı, Kargoya Verildi,
  Tamamlandı, İptal Edildi, İptal Geri Alındı, İade Edildi, Reddedildi, Süresi Doldu,
  Ters İbraz, Başarısız, Durduruldu, Hükümsüz)
- KDV dahil/hariç fiyat gösterimi, vergi sınıfı bazlı hesaplama
- Miktar bazlı kademeli indirim (adet indirimi), kupon/kampanya sistemi
- Ürün karşılaştırma, favori listesi, ürün yorumu ve soru-cevap
- Ortaklık/affiliate sistemi (komisyonlu referans satışı) — opsiyonel modül
- SEO: sayfa/ürün bazlı meta başlık/açıklama/anahtar kelime, Google Merchant alanları
  (age group, gender), mevcut URL slug yapısının korunması gerekiyor
- Yasal: KVKK ve Mesafeli Satış Yönetmeliği'ne uygun CMS sayfaları (Gizlilik Politikası,
  Mesafeli Satış Sözleşmesi, İptal/İade Koşulları, Ön Bilgilendirme Formu)
- Kargo: alıcı ödemeli kargo, kargo takip numarası ile sipariş takibi

Mevcut BizimHesap muhasebe sisteminde tespit edilen yapı:
- Cari hesaplar (müşteri/tedarikçi ortak, 389 kayıt), her biri açık bakiye ve çek/senet
  bakiyesi taşıyor
- Ürünler adet dışında m² (metrekare) ve mt (metre tül) gibi birimlerle de stoklanabiliyor
- Satış belgeleri bir durum akışı izliyor: Taslak → İrsaliyeleşmiş → Faturalaşmış
  (E-Fatura) / Faturalaşmış (E-Arşiv)
- Satış kayıtları bir "Sipariş No" alanıyla kaynak siparişe bağlanabiliyor
- Kasa, Banka, Çek, Senet ayrı ayrı takip ediliyor; dashboard'da Varlıklar/Borçlar
  özet kartları var
- Kendi bünyesinde Trendyol, Hepsiburada, Amazon, N11, Etsy, AliExpress, Ozon, Wish
  gibi onlarca pazaryeri/e-ihracat kanalıyla ürün eşleştirme, listeleme, fiyat
  güncelleme entegrasyonu sunuyor

ONAYLANMIŞ MİMARİ KARARLAR (bunları tartışmaya açma, doğrudan uygula)
1. Storefront: Server-Side Rendering (ASP.NET Core MVC/Razor Pages) — SEO önceliklidir.
2. Admin panel: Blazor Server.
3. Veritabanı: Microsoft SQL Server.
4. ÖDEME, KARGO, PAZARYERİ ve e-FATURA — DÖRDÜ DE aynı ortak mimari kalıba uyacak: bir
   "Provider Registry" (Sağlayıcı Kayıt Defteri). Her sağlayıcı tipi için ortak bir
   interface tanımla (IPaymentGateway, ICargoProvider, IMarketplaceConnector,
   IEInvoiceProvider); her connector kendi gerekli config alanlarını (anahtar, etiket,
   tip, zorunlu mu) deklare etsin. Şu connector'ların TÜMÜNÜ kodla ve registry'e kaydet:
     - Ödeme: iyzico, PayTR, Param, firmanın kendi banka/POS hesabı, PayPal, Stripe
     - Kargo: Yurtiçi Kargo, Aras Kargo, MNG Kargo, DHL, UPS, FedEx
     - Pazaryeri: Trendyol, Hepsiburada, N11, İdefix, Amazon
     - e-Fatura: BizimHesap (apidocs.bizimhesap.com API'si), Nilvera, Uyumsoft, Foriba,
       İzibiz
   HİÇBİRİ VARSAYILAN OLARAK AKTİF OLMASIN. Admin panelde jenerik bir "Entegrasyonlar"
   ekranı yap: her sağlayıcı tipi için bir sekme, her connector bir kart, karta
   tıklanınca o connector'ın deklare ettiği alanlara göre OTOMATİK/DİNAMİK bir form
   render edilsin (metadata-güdümlü form — yeni bir connector eklendiğinde admin
   panelde elle yeni bir form yazmaya gerek kalmamalı). Kullanıcı bilgileri girip
   bağlantıyı test ettiğinde sağlayıcı "Aktif" olsun. Ödeme sağlayıcısı seçiminde son
   karar checkout'ta MÜŞTERİYE aittir (aktif olanlar arasından); kargo/pazaryeri/
   e-Fatura sağlayıcısı seçimi ise admin'in hangi sağlayıcıyı aktifleştirdiğine bağlıdır.
5. Sistem e-ihracata uygun olacak: hedef diller TÜRKÇE, İNGİLİZCE, ALMANCA, FRANSIZCA,
   HOLLANDACA, İSPANYOLCA ve ARAPÇA. ARAPÇA İÇİN SAĞDAN-SOLA (RTL) DÜZEN DESTEĞİ
   ZORUNLUDUR — Storefront'ta CSS mantıksal özellikler + dir="rtl", Flutter'da
   Directionality/Bidi desteği. Ayrıca: çok para birimli fiyat gösterimi (TRY temel,
   güncel kur ile diğer para birimleri), esnek/uluslararası adres şeması, ürünlerde
   HS/GTİP kodu alanı, yurt dışı satışta KDV istisnası hesaplama, gerekiyorsa GDPR'a
   uygun veri işleme (AB dilleri hedeflendiği için önemli).
6. Mobil ve web'in ortak kullandığı, versiyonlanmış (api/v1) bir REST API katmanı
   (Dekorras.Api) olacak — Swagger/OpenAPI ile tam dokümante, JWT + refresh token
   ile kimlik doğrulamalı.
7. Bu API'yi tüketen, ayrı bir proje olarak **Flutter (Dart)** tabanlı bir mobil
   uygulama geliştirilecek (iOS + Android tek kod tabanından).
8. Sisteme, BizimHesap'ın yapısından esinlenilen **native bir Muhasebe modülü**
   eklenecek (Cari Hesap, Kasa/Banka/Çek/Senet, Fatura/İrsaliye, Teklif, Gider,
   Raporlar). Admin paneline girişte kullanıcıyı **"Muhasebe Modülü"** ve
   **"E-Ticaret Modülü"** seçeneklerini gösteren bir **modül seçim ekranı**
   karşılayacak; kullanıcı yalnızca yetkili olduğu modül(ler)i görecek (RBAC).
   E-Ticaret'teki bir sipariş tamamlandığında Muhasebe modülünde otomatik olarak
   ilgili cari hesaba bağlı bir fatura/irsaliye taslağı oluşacak. Resmi e-Fatura/
   e-Arşiv gönderimi GİB sertifikalı bir "özel entegratör" gerektirdiğinden, bunu
   sıfırdan kurmaya ÇALIŞMA — madde 4'teki Provider Registry üzerinden sertifikalı
   üçüncü parti bir entegratörün API'sine bağlan.

HEDEF MİMARİ
.NET 9 / ASP.NET Core 9 üzerinde ONION (katmanlı/soğan) mimari kullan. Kesin kural:
bağımlılıklar her zaman içe doğru akar, Domain katmanının HİÇBİR dış bağımlılığı
(EF Core, ASP.NET Core, üçüncü parti paket) olmamalıdır.

Monorepo / solution yapısı:
  backend/Dekorras.sln
  backend/src/Core/Dekorras.Domain          → Entities (Katalog, Sipariş, Muhasebe/Cari/
                                               Fatura/Kasa, IntegrationProvider dahil),
                                               ValueObjects, Enums, DomainEvents, Exceptions
  backend/src/Core/Dekorras.Application     → CQRS (MediatR) Command/Query Handlers
                                               (E-Ticaret ve Muhasebe use case'leri), DTO'lar,
                                               FluentValidation validator'ları, Provider
                                               Registry sözleşmeleri (IPaymentGateway,
                                               ICargoProvider, IMarketplaceConnector,
                                               IEInvoiceProvider) + IFileStorage, IEmailSender,
                                               IPushNotificationSender, ICacheService,
                                               IExchangeRateProvider, Mapster/AutoMapper profilleri
  backend/src/Infrastructure/Dekorras.Persistence     → EF Core DbContext (SQL Server), Fluent
                                                         API configuration'ları, Migrations,
                                                         Repository + UnitOfWork implementasyonları
  backend/src/Infrastructure/Dekorras.Infrastructure  → ProviderRegistry çekirdek implementasyonu +
                                                         PaymentProviders/ (iyzico, PayTR, Param,
                                                         PayPal, Stripe), CargoProviders/ (Yurtiçi,
                                                         Aras, MNG, DHL, UPS, FedEx),
                                                         MarketplaceConnectors/ (Trendyol,
                                                         Hepsiburada, N11, İdefix, Amazon),
                                                         EInvoiceProviders/ (BizimHesap, Nilvera,
                                                         Uyumsoft, Foriba, İzibiz), Redis cache,
                                                         Blob/S3 dosya depolama, e-posta, Firebase
                                                         push bildirim, döviz kuru servisi,
                                                         Hangfire job'ları
  backend/src/Presentation/Dekorras.Api         → REST API (api/v1), JWT auth, Swagger/OpenAPI —
                                                   Admin (her iki modül), Storefront, Flutter
                                                   mobil ve pazaryeri senkron servisinin TEK
                                                   ortak backend'i, pazaryeri webhook uç noktaları
  backend/src/Presentation/Dekorras.Admin       → Yönetim paneli — Blazor Server;
                                                   Pages/ModuleSelector → giriş sonrası Muhasebe/
                                                   E-Ticaret seçim ekranı;
                                                   Pages/Integrations → dinamik, metadata-güdümlü
                                                   "Entegrasyonlar" ekranı (madde 4);
                                                   Areas/ECommerce → katalog, sipariş, kampanya;
                                                   Areas/Accounting → cari, kasa/banka, fatura/
                                                   irsaliye, teklif, gider, muhasebe raporları
  backend/src/Presentation/Dekorras.Storefront  → Mağaza ön yüzü — Server-Side Rendering
                                                   (MVC/Razor Pages), sitemap.xml, canonical URL,
                                                   JSON-LD ürün şeması, TR/EN/DE/FR/NL/ES/AR
                                                   (Arapça RTL dahil) dil desteği
  backend/src/Presentation/Dekorras.MarketplaceSync → (opsiyonel, ölçek büyüdükçe) pazaryeri
                                                   senkron job'larını API'den ayrıştıran bağımsız
                                                   Worker Service host'u
  backend/tests/Dekorras.Domain.Tests, Dekorras.Application.Tests, Dekorras.IntegrationTests
                                                 → xUnit + FluentAssertions + NSubstitute +
                                                   Testcontainers (SQL Server)
  mobile/dekorras_app/                          → Flutter uygulaması; presentation/domain/data
                                                   katmanlı iç yapı; Riverpod veya BLoC; Dio ile
                                                   Dekorras.Api tüketimi; Firebase Cloud Messaging;
                                                   Directionality/Bidi ile Arapça RTL desteği

DOMAIN MODÜLLERİ (bounded context bazında entity listesi)
1. Katalog: Category (self-referencing ağaç, sınırsız derinlik), Product (birim/UoM alanı:
   adet/m²/mt), ProductVariant, ProductAttribute, Brand, ProductImage, ProductVideo,
   QuantityDiscount, ProductReview, ProductQuestion. Product alanları: çok dilli ad/açıklama
   (ProductTranslation — TR/EN/DE/FR/NL/ES/AR), HS/GTİP kodu (e-ihracat), fiyat (TRY) +
   KDV sınıfı, stok adedi, minimum satış adedi, stok dışı davranışı, boyut/ağırlık, yayın
   tarihi, durum, meta robots, Google Merchant alanları, sıralama düzeni.
2. Sepet & Sipariş: Cart, CartItem, Order (OrderSource enum: Web/Mobil/Trendyol/Hepsiburada/
   N11/İdefix/Amazon), OrderItem, OrderStatusHistory (14 durumlu state machine), Coupon,
   GiftVoucher.
3. Müşteri: Customer, CustomerGroup (Bireysel/Kurumsal), Address (ülkeye göre esnek format),
   Affiliate, WishlistItem.
4. Entegrasyon / Provider Registry: IntegrationProvider (tip: Payment/Cargo/Marketplace/
   EInvoice, durum: Yapılandırılmadı/Aktif/Hata, şifreli config), IntegrationConfigField
   (connector'ın deklare ettiği alan tanımı), IntegrationHealthCheckLog.
5. Ödeme: Payment, PaymentProvider (IntegrationProvider'a bağlı), Transaction.
6. Kargo: ShippingMethod, ShippingRate, ShipmentTracking, CustomsDeclaration.
7. Pazaryeri Entegrasyonları: MarketplaceAccount, MarketplaceListing, MarketplaceCategoryMapping,
   MarketplaceOrder, MarketplaceSyncLog.
8. Muhasebe: LedgerAccount (Cari — müşteri/tedarikçi ortak, açık bakiye + çek/senet
   bakiyesi), LedgerTransaction, Invoice (durum: Taslak/İrsaliyeleşmiş/Faturalaşmış,
   OrderId referansı), Waybill, Quote (Teklif), CashRegister, BankAccount, Check,
   PromissoryNote, Expense, EInvoiceLog.
9. Lokalizasyon: Language (TR/EN/DE/FR/NL/ES/AR + IsRightToLeft bayrağı), Translation,
   Currency, ExchangeRate.
10. İçerik/CMS: CmsPage, BlogPost, Banner, MenuItem.
11. Pazarlama: Campaign, PromotionRule, NewsletterSubscriber.
12. Sistem: AdminUser, Role, Permission (RBAC — modül erişimi dahil), Setting, AuditLog.
13. Bildirim: EmailTemplate, PushNotification, NotificationLog.
"Pazaryeri" (Dekorras'ın kendisinin çoklu satıcılı bir platforma dönüşmesi) modülünü
ilk sürümde KAPSAM DIŞI bırak; bunu madde 4'teki "hazır pazaryerlerine satıcı olarak
entegrasyon" ile KARIŞTIRMA — ikisi farklı şeylerdir.

TESLİMATLAR
1. Yukarıdaki solution yapısında çalışan bir .NET 9 çözümü + ayrı bir Flutter projesi
2. EF Core Code-First migration'ları (SQL Server) + gerçekçi seed data
3. CQRS pattern ile yazılmış Application katmanı
4. Repository + UnitOfWork implementasyonları
5. REST API: kimlik doğrulama, ürün/kategori/sipariş/müşteri/cari/fatura endpoint'leri,
   Provider Registry'nin aktif sağlayıcı listesini dönen endpoint'ler, pazaryeri webhook
   uç noktaları, Swagger dokümantasyonu, RFC 7807 hata formatı
6. Admin panel (Blazor Server): Modül Seçim Ekranı, dinamik Entegrasyonlar ekranı (TÜM
   connector'lar için — hiçbiri önceden aktif değil), E-Ticaret alanı (katalog, sipariş,
   kampanya, müşteri, CMS), Muhasebe alanı (cari, kasa/banka/çek/senet, fatura/irsaliye,
   teklif, gider, raporlar)
7. Storefront (SSR): TR/EN/DE/FR/NL/ES/AR (Arapça RTL) dil desteğiyle anasayfa, katalog,
   ürün detay, sepet, checkout (aktif ödeme sağlayıcıları arasından müşteri seçimi), üyelik,
   blog, CMS, arama, dil/para birimi seçici
8. Provider Registry'ye kayıtlı TÜM connector implementasyonları (ödeme: iyzico/PayTR/
   Param/PayPal/Stripe; kargo: Yurtiçi/Aras/MNG/DHL/UPS/FedEx; pazaryeri: Trendyol/
   Hepsiburada/N11/İdefix/Amazon; e-Fatura: BizimHesap/Nilvera/Uyumsoft/Foriba/İzibiz) —
   kodları yazılmış, testleri geçmiş, ama hiçbiri varsayılan aktif olmadan teslim edilir
9. Sipariş tamamlandığında Muhasebe modülünde otomatik fatura/irsaliye taslağı oluşturan
   bir domain event handler'ı
10. Flutter mobil uygulama: katalog, sepet, checkout, üyelik/JWT, sipariş takibi, push
    bildirim, çok dil (Arapça RTL dahil)
11. Docker Compose ile tek komutla ayağa kalkan geliştirme ortamı (API + SQL Server + Redis)
12. GitHub Actions CI pipeline'ları (backend + Flutter)
13. Unit + entegrasyon test kapsamı (checkout, en az bir aktifleştirilmiş ödeme/kargo/
    pazaryeri/e-Fatura connector'ı ile uçtan uca senaryo dahil)
14. README: mimari açıklaması, kurulum adımları, ortam değişkenleri, "yeni bir connector
    (ör. yeni bir pazaryeri) nasıl eklenir" rehberi

ADIM ADIM UYGULAMA SIRASI
1. Solution/proje iskeletini oluştur, katman referanslarını Onion kuralına göre bağla.
2. Domain katmanını yaz: yukarıdaki entity'ler, value object'ler, enum'lar, domain
   event'leri (OrderCompleted → InvoiceDraftRequested dahil), özel exception'lar.
3. Application katmanını yaz: Provider Registry sözleşmelerini (IPaymentGateway,
   ICargoProvider, IMarketplaceConnector, IEInvoiceProvider) ve ConfigFieldDefinition
   mekanizmasını ÖNCE tasarla — diğer tüm connector'lar buna göre yazılacak; her modül
   için Command/Query + Handler + Validator + DTO.
4. Persistence katmanını yaz: SQL Server DbContext, Fluent API configuration'ları, ilk
   migration, seed data.
5. Infrastructure katmanını yaz: ProviderRegistry çekirdek implementasyonu, ardından
   TÜM connector'ları (ödeme, kargo, pazaryeri, e-Fatura) bu sözleşmelere göre yaz;
   döviz kuru servisi, Redis cache, dosya depolama, e-posta, Firebase push, Hangfire.
6. API projesini yaz: controller'lar, JWT auth, Swagger, Provider Registry'nin aktif
   sağlayıcı listesini dönen endpoint'ler, pazaryeri webhook uç noktaları.
7. Admin panelini (Blazor Server) yaz: önce Modül Seçim Ekranı + RBAC, sonra jenerik
   metadata-güdümlü Entegrasyonlar ekranını (ConfigFieldDefinition'dan otomatik form
   üreten component), sonra E-Ticaret ve Muhasebe alanlarını oluştur.
8. Storefront'u (SSR) yaz: çok dil (Arapça RTL dahil) desteğiyle, SEO'yu önceleyerek.
9. Flutter mobil uygulamasını yaz: aynı API'yi tüketerek, çok dil/RTL desteğiyle.
10. Test paketini yaz: Domain/Application birim testleri; en az bir ödeme, bir kargo,
    bir pazaryeri ve bir e-Fatura connector'ını admin panelden aktifleştirip uçtan uca
    çalıştıran entegrasyon testleri.
11. Docker Compose ve CI pipeline'larını ekle.
12. Veri taşıma script'i/aracı: KobiDirekt ve BizimHesap'tan export edilecek verileri
    yeni şemaya aktaracak bir import aracı yaz.

KABUL KRİTERLERİ
- Domain katmanı derlendiğinde EF Core, ASP.NET Core veya başka bir framework paketine
  referans içermemeli.
- Tüm yazma işlemleri FluentValidation ile doğrulanmalı; hatalar RFC 7807 formatında dönmeli.
- Category ağacı sınırsız derinlikte olmalı ve admin panelde sürükle-bırak ile sıralanabilmeli.
- Product entity'si minimum satış adedi, stok dışı davranışı, KDV dahil/hariç fiyat,
  Bireysel/Kurumsal fiyat farkını uygulamalı.
- Sipariş durum geçişleri (14 durum) bir state machine ile kısıtlanmalı, kaynağı
  (Web/Mobil/pazaryeri) fark etmeksizin.
- **Provider Registry çalışır durumda olmalı:** admin panelde hiçbir sağlayıcı önceden
  aktif olmadan sistem ayağa kalkmalı; bir sağlayıcı (ör. iyzico) admin panelden form
  doldurularak aktifleştirildiğinde, kod değişikliği/deploy YAPILMADAN checkout'ta
  görünür olmalı. Yeni bir connector eklemek yalnızca yeni bir sınıf yazıp registry'e
  kaydetmekle mümkün olmalı — Admin UI'da elle yeni ekran yazmaya gerek kalmamalı.
- Storefront ve mobil uygulama TR/EN/DE/FR/NL/ES/AR ile çalışmalı; Arapça seçildiğinde
  arayüz otomatik RTL'e geçmeli (metin hizalama, ikon/layout yönü dahil).
- **Modül seçim ekranı** kullanıcının RBAC yetkisine göre doğru kart(lar)ı göstermeli;
  yetkisiz bir kullanıcı diğer modülün URL'sine doğrudan erişmeye çalışsa bile
  yetkilendirme katmanında engellenmeli.
- Bir sipariş "Tamamlandı" durumuna geçtiğinde Muhasebe modülünde otomatik olarak
  ilgili cari hesaba bağlı bir fatura/irsaliye taslağı oluşmalı; en az bir e-Fatura
  entegratörü aktifleştirildiğinde uçtan uca bir e-Fatura kesilebilmeli.
- API tamamen Swagger üzerinden keşfedilebilir olmalı; kimlik doğrulama olmadan
  hiçbir yazma endpoint'ine erişilememeli.
- Mevcut sitedeki ürün/kategori URL slug yapısı korunmalı veya 301 yönlendirme
  haritası ile eşlenmeli.
- `docker compose up` komutu ile tüm backend sistemi tek komutla ayağa kalkmalı.
- CI pipeline'ları her push'ta build + testleri otomatik çalıştırmalı.

KISITLAR VE DİKKAT EDİLECEK NOKTALAR
- KVKK, Mesafeli Satış Yönetmeliği ve (AB dillerine satışta) GDPR'a uyumlu veri işleme
  ve sözleşme akışlarını koru.
- Ödeme sağlayıcı entegrasyonlarında PCI-DSS kapsamına girmemek için kart bilgilerini
  kendi sunucunda tutma; sağlayıcı API anahtarlarını Data Protection API ile şifreli sakla.
- Her pazaryerinin kendi API rate limit'lerine ve zorunlu alan/kategori şablonlarına uyulmalı.
- **Resmi e-Fatura/e-Arşiv gönderimi için kendi GİB özel entegratörlüğünü kurmaya
  ÇALIŞMA** — her zaman sertifikalı üçüncü parti bir entegratörün API'sine bağlan.
- **Hiçbir sağlayıcıyı (ödeme/kargo/pazaryeri/e-Fatura) hard-code aktif etme veya
  varsayılan kimlik bilgisiyle deploy etme** — Provider Registry mimarisinin bütün
  amacı budur.
- Arapça RTL desteğini "sona bırakılabilir kozmetik iş" olarak görme — layout, form
  input yönü ve sayı/tarih biçimlendirmesini etkiler, ayrı bir test kapsamı gerektirir.
- İlk sürümde "Pazaryeri" (Dekorras'ın kendisinin çoklu satıcı platformuna dönüşmesi) ve
  "Ortaklık/Affiliate" modüllerini opsiyonel faz olarak ele al, MVP kapsamına zorunlu
  dahil etme.
- Flutter uygulamasının mağaza yayını (App Store/Google Play/Firebase) hesap açma ve
  ücretli kayıt işlemleri gerektirir; bunlar kod teslimatının dışında, firma tarafından
  tamamlanması gereken operasyonel adımlardır (bkz. §16).
- Her yeni özellik için önce Domain'de iş kuralını modelle, sonra Application'da
  use case'i yaz, en son Infrastructure/Presentation'a in — bu sırayı tersine çevirme.
```

## 15\. Sonraki Adım

§1\-§14'teki mimari kararların çoğu netleşmiştir. Kalan operasyonel maddeler:

1. **Ödeme, kargo ve pazaryeri sağlayıcıları:** Bunların hiçbiri artık ilk sürümde "seçilmesi gereken" kararlar değil — §4.1'deki Provider Registry mimarisi sayesinde tüm connector'lar (iyzico/PayTR/Param/PayPal/Stripe, Yurtiçi/Aras/MNG/DHL/UPS/FedEx, Trendyol/Hepsiburada/N11/İdefix/Amazon) geliştirme sürecinde yazılacak; firma hangisini/hangilerini ne zaman kullanacağına kendi ticari anlaşmalarına göre admin panelden karar verecek. Geriye kalan tek iş: her aktifleştirilecek sağlayıcı için satıcı/üye işyeri hesabı ve API anahtarı başvurusunun (firma tarafından) yapılması.
2. **e\-Fatura entegratörü:** Mevcut BizimHesap hesabının API erişim koşulları (`apidocs.bizimhesap.com`) BizimHesap destek/satış ekibiyle teyit edilmeli — kurumsal/API planı varsa bu entegrasyon önceliklendirilebilir; yoksa Nilvera/Uyumsoft/Foriba/İzibiz gibi bağımsız bir entegratörle ilerlenir. Her iki durumda da admin panelde ilgili connector kartı hazır olacaktır.
3. **Hedef ihracat dilleri netleşti:** TR, EN, DE, FR, NL, ES, AR — bu belgeye ve promptuna işlenmiştir; Arapça RTL desteği ayrı bir mühendislik kalemi olarak planlanmıştır (§7).
4. §13'teki yol haritasına göre Faz 0 (keşif) toplantısının planlanması.
5. Flutter mobil mağaza yayın hazırlığı için **§16'daki kontrol listesine** bakınız — bu adımlar hesap sahibi tarafından bizzat tamamlanması gereken, ödeme ve resmi kimlik doğrulaması içeren işlemlerdir.

## 16\. Mobil Mağaza Yayın Hazırlığı — Kontrol Listesi

**Önemli not:** Apple Developer Program'a kayıt (yıllık 99 USD ücretli), Google Play Console'a kayıt (tek seferlik 25 USD ücretli) ve bunlara bağlı kimlik/ödeme bilgisi girişleri, hesap sahibinin kendisi tarafından tamamlanması gereken işlemlerdir — bunlar ödeme ve resmi hesap/kimlik doğrulaması içerdiğinden bu belgeyi hazırlayan taraf (Claude) adınıza gerçekleştiremez. Firebase projesi de bir Google hesabı üzerinden açıldığından aynı şekilde hesap sahibi tarafından kurulmalıdır. Aşağıda, bu üç kaydı tek oturumda ve hızlıca tamamlamanız için ihtiyaç duyacağınız bilgiler ve adımlar listelenmiştir; sistemde görülen `dekorras@gmail.com` adresi Google tarafı (Play Console \+ Firebase) için doğrudan kullanılabilir.

**A) Google Play Console (Android yayını \+ Firebase için de kullanılabilir):**

1. `dekorras@gmail.com` ile [play.google.com/console](https://play.google.com/console) adresinden kayıt başlatılır.
2. Hesap türü **"Kuruluş" (Organization)** seçilmeli (bireysel değil) — firma unvanı (ör. "Dekorras Mimarlık Mühendislik" veya faaliyet gösterdiği tam ticari unvan), vergi/işletme kimlik numarası ve adres bilgisi istenecektir.
3. Tek seferlik 25 USD kayıt ücreti bir banka/kredi kartıyla ödenir.
4. Kimlik doğrulama (D\-U\-N\-S numarası gerekmez, ancak resmi işletme belgesi istenebilir) 1\-3 iş günü sürebilir.

**B) Apple Developer Program (iOS yayını):**

1. Mevcut bir Apple ID ile veya yeni oluşturulacak bir Apple ID ile [developer.apple.com/programs](https://developer.apple.com/programs/enroll/) üzerinden başvuru yapılır.
2. **Kuruluş (Organization)** hesabı için bir **D\-U\-N\-S numarası** gereklidir — firma adına henüz yoksa Apple'ın kendi D\-U\-N\-S başvuru aracından ücretsiz talep edilebilir (temin süresi birkaç gün ile birkaç hafta arasında değişebilir); bu adım en çok zaman alan kısımdır, en erken başlatılması önerilir.
3. Yıllık 99 USD program ücreti kredi kartıyla ödenir.
4. Başvuruyu yapan kişinin firmada yasal imza yetkisi olması (veya yetkiliyi bu süreçte onaylatması) gerekir.

**C) Firebase Projesi (push bildirim için, Google Play ile aynı hesapla):**

1. `dekorras@gmail.com` ile [console.firebase.google.com](https://console.firebase.google.com) adresinden "Proje Ekle" ile yeni bir proje açılır (ör. `dekorras-app`) — ücretsiz Spark planı push bildirim (Firebase Cloud Messaging) için yeterlidir, kart bilgisi gerekmez.
2. Projeye Android (`com.dekorras.app` gibi bir paket adıyla) ve iOS (Apple Developer hesabından alınacak Bundle ID ile) uygulamaları eklenir; `google-services.json` (Android) ve `GoogleService-Info.plist` (iOS) dosyaları indirilip Flutter projesine eklenir — bu adım geliştirme ekibiyle birlikte yapılabilir.

**Önerilen sıralama:** D\-U\-N\-S numarası başvurusu (en uzun süren adım) hemen başlatılmalı → paralelde Google Play Console kaydı tamamlanmalı → Firebase projesi Play Console ile aynı oturumda kurulmalı → D\-U\-N\-S onaylanır onaylanmaz Apple Developer başvurusu tamamlanmalı. Bu sıralamayla üç kayıt da geliştirme ekibinin Faz 10'a (§13) ulaşmasından çok önce hazır olabilir.
