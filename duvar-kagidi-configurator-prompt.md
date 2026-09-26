# ASP.NET Core 9 – Ölçüye Özel Duvar Kağıdı Konfigüratörü (Geliştirme Prompt'u)

> Bu prompt'u doğrudan bir AI kodlama asistanına (Claude, Copilot, Cursor vb.) verebilirsin. İki referans sitenin güçlü yanlarını birleştirir ve birinin eksik bıraktığını diğerinden tamamlar.

---

## 0. Referans Analizi (bağlam için – asistana da verilebilir)

| Özellik | duvarkagidimarketi.com (WooCommerce) | duvarkapla.com (Shopify) | Hedef sistem |
|---|---|---|---|
| Birimler | cm, inç | m/cm, inç, ft | **cm, m, inç, ft** |
| Min. ölçü doğrulama | ✅ min 10 cm | ❌ | ✅ |
| Canlı alan (m²) gösterimi | ✅ | ❌ | ✅ |
| Kesim/montaj payı | ✅ otomatik +5 cm | ⚠️ sadece öneri (5–10 cm) | ✅ malzemeye göre ayarlanabilir, otomatik |
| Malzeme bazlı ₺/m² fiyat tablosu | ✅ 8 malzeme, şeffaf | ❌ sadece "Başlangıç 40₺/m²" | ✅ |
| Malzeme teknik özellikleri (gr/m², panel eni, yangın sınıfı) | ✅ | ❌ | ✅ |
| Kırpma aracı | ✅ "Kırpmayı aktifleştir" | ✅ | ✅ oran kilitli kırpma |
| Esnet / Kırp (fit mode) | ❌ | ✅ | ✅ |
| Ayna (yatay çevirme) | ❌ | ✅ | ✅ |
| Siyah-beyaz filtre | ❌ | ✅ | ✅ (+ sepya opsiyonel) |
| URL ile durum paylaşımı (`?material=&unit=&w_cm=&h_cm=`) | ❌ | ✅ | ✅ |
| Oda fotoğrafında önizleme | ❌ (sadece sipariş sonrası önizleme maili) | ✅ kullanıcı başı 10 ücretsiz | ✅ + sipariş sonrası onay önizlemesi |
| Tasarım değişiklik talep formu (görsel yükleme) | ❌ | ✅ 2 iş günü yanıt | ✅ |
| Numune siparişi | ✅ 4 malzeme | ✅ 200₺ | ✅ |
| Tutkal upsell / eşik üstü ücretsiz | ✅ 1000₺ üzeri ücretsiz | ❌ | ✅ |
| Ücretsiz kargo eşiği | ✅ | ✅ 5.000₺ | ✅ admin'den ayarlanır |
| Kupon kodu | ❌ | ✅ (15OFF) | ✅ |
| Üretim + teslim tarihi hesabı (hafta sonu kuralı) | ✅ | ⚠️ sadece "3–5 gün" | ✅ tahmini tarih aralığı |
| Etiket bazlı katalog (renk, oda, stil, tema, ton) | ❌ | ✅ | ✅ faceted filtre |
| Stok | ✅ | ❌ | ✅ (numune/tutkal gibi fiziksel ürünler için) |
| Hazır oda sahnelerinde duvarda gösterim | ❌ | ⚠️ sadece kullanıcının kendi fotoğrafı | ✅ hazır sahneler + kendi fotoğrafı |
| Duvar önizlemesinde poster listesinden hızlı değiştirme | ❌ | ❌ | ✅ |
| "Duvarımda dene" listesi / karşılaştırma | ❌ | ❌ | ✅ |
| Favoriler | ❌ | ❌ | ✅ |
| Toplu poster yükleme (admin) | ❌ | ❌ | ✅ |
| Her ürüne "Duvarında Gör" bağlantısı (deep link + modal + gömülebilir widget) | ❌ | ⚠️ sadece fotoğraf yükleme sayfası | ✅ |

---

## 1. PROMPT (bundan sonrasını kopyala)

Sen kıdemli bir .NET mimarısın. **ASP.NET Core 9** ile, ölçüye özel üretilen duvar kağıdı / poster duvar kağıdı satan bir e-ticaret sitesinin **ürün konfigüratörü, fiyatlama motoru, sepet ve sipariş akışını** geliştir. Kod üretime hazır, test edilebilir ve Türkçe (tr-TR) arayüzlü olsun.

### 1.1 Teknoloji ve Mimari
- .NET 9, C# 13, **Clean Architecture**: `Domain`, `Application`, `Infrastructure`, `Web` (+ `Tests`).
- Web katmanı: **Razor Pages** (SEO dostu ürün sayfaları) + konfigüratör için **Minimal API** uç noktaları (`/api/...`).
- Veri: **EF Core 9** + PostgreSQL (SQL Server'a geçilebilir olsun). Migration'lar dahil.
- Görsel işleme: **SixLabors.ImageSharp** (sunucu tarafı üretim dosyası, thumbnail, filtre).
- Ön yüz: sade JS modülleri (veya Alpine.js) + **Cropper.js**; canvas tabanlı canlı önizleme. SPA framework kullanma.
- Doğrulama: **FluentValidation**. Eşleme: elle veya Mapster. CQRS için MediatR opsiyonel – gereksizse kullanma.
- Kimlik: ASP.NET Core Identity (misafir sepet + üye). Önbellek: `HybridCache` (.NET 9).
- Loglama: Serilog. Hata yönetimi: `ProblemDetails` + global exception handler.
- Para: her yerde `decimal`, TRY, `tr-TR` formatı (`1.499,00 ₺`). Yuvarlama `MidpointRounding.AwayFromZero`, 2 hane.

### 1.2 Domain Modeli
- **Product**: Id, Slug, Title, Description, Sku, ImageOriginalUrl, ImageWidthPx, ImageHeightPx, Dpi, IsActive, Tags (çoka-çok), SeoTitle/SeoDescription.
  - **ProductType**: `Mural` (tek parça poster, ölçüye göre kırpılır/esnetilir) | `Pattern` (tekrarlı desen).
  - Pattern için: RepeatWidthCm, RepeatHeightCm, RepeatType (`Straight` | `HalfDrop`).
  - DominantColors (yükleme sırasında otomatik çıkarılan 3–5 HEX renk), AspectRatio, PopularityScore, CreatedAt.
  - Türev görseller: `ThumbUrl` (400px), `ListUrl` (800px), `PreviewUrl` (2000px, filigranlı), `LqipBase64` (bulanık yer tutucu).
- **RoomScene** (hazır oda sahnesi): Id, Name, RoomType (salon, yatak odası, çocuk odası, ofis, mutfak…), BaseImageUrl (boş duvarlı fotoğraf), ShadowMapUrl (gri tonlu ışık/gölge katmanı), ForegroundMaskUrl (duvarın önündeki mobilya/bitkiler – şeffaf PNG), WallQuad (duvarın 4 köşesi, piksel), RealWallWidthCm / RealWallHeightCm (ölçek için), SortOrder, IsActive, IsDefault.
- **EmbedClient** ("Duvarında Gör" widget'ını kullanan harici site): PublicKey, Name, AllowedOrigins, AllowedImageHosts, DailyQuota, IsActive.
- **ExternalImage**: EmbedClientId, SourceUrl, yerel türev URL'leri, genişlik/yükseklik, CreatedAt (harici siteden gelen, sistemde kayıtlı olmayan posterler için).
- **Favorite**: UserId (veya misafir anahtarı), ProductId, CreatedAt.
- **TryOnList** ("Duvarımda Dene" listesi): Owner (UserId veya misafir çerez anahtarı), Items (ProductId, sıra, isteğe bağlı konfigürasyon: material, fit, mirror, filter, crop), maks. 12 öğe; misafir listesi girişte üye hesabına birleştirilir.
- **Tag**: `Group` (color, room, style, theme, nature, tone, density, scale, light) + `Value` → `color:green`, `room:salon` gibi.
- **Material**: Code, Name, PricePerM2, PanelWidthCm (örn. 50/100/140), MaxHeightCm, WeightGsm, FireRating (örn. B-s1,d0), IsSelfAdhesive, RequiresGlue, BleedCm (varsayılan 5), MinBillableAreaM2 (örn. 1,00), SortOrder, IsActive, Açıklama/özellik listesi.
  - Seed verisi (₺/m²): Dokusuz 699, Dokulu 849, Tekstil 999, Kendiliğinden Yapışkanlı Folyo 1.099, Premium Tekstil 1.399, Canvas Yapışkanlı 1.499, Hasır Dokulu 1.699, Gümüş/Gold 3.499.
- **ProductMaterialOverride** (opsiyonel): ürün bazında malzeme fiyatı/izin.
- **Configuration** (value object): WidthCm, HeightCm, Unit (girilen birim), MaterialCode, FitMode (`Crop` | `Stretch`), Mirror (bool), Filter (`None` | `Grayscale` | `Sepia`), CropRect (x, y, w, h – orijinal görsel pikselinde, normalize 0–1 olarak da tut).
- **Cart / CartItem**: CartItem konfigürasyonu JSON olarak saklar + fiyat anlık görüntüsü (`PriceSnapshot`) + `ConfigHash`.
- **Order / OrderItem**: sipariş anındaki tüm hesaplamaları dondurur (malzeme adı, ₺/m², faturalanan m², panel sayısı, pay, indirim).
- **SampleProduct** (numune) ve **Accessory** (tutkal, spatula vb.) – stoklu fiziksel ürünler.
- **Coupon**: Code, Type (Percent/Fixed), Value, MinCartTotal, StartsAt/EndsAt, UsageLimit, PerUserLimit.
- **ShippingRule**: FreeShippingThreshold (örn. 5.000 ₺), FlatFee; **GlueRule**: gerekli malzemede tutkal öner, sepet ≥ 1.000 ₺ ise tutkal ücretsiz.
- **RoomPreview**: UserId, RoomPhotoUrl, 4 köşe koordinatı (perspektif), ResultUrl, CreatedAt. Üye başına ücretsiz kota (varsayılan 10, ayarlanabilir).
- **DesignRequest**: Ad, E-posta, ProductId, RequestType (`OzelOlcu`, `RenkDegisikligi`, `GorselDuzenlemeKirpma`, `OgeEkleKaldir`, `Diger`), Mesaj, Ekler (JPG/PNG/WebP, max 10 MB), Status, SLA (2 iş günü), AdminNotu.
- **ProductionProof**: sipariş sonrası müşteriye gönderilen ölçüye uyarlanmış önizleme ve onay durumu (`Beklemede`, `Onaylandı`, `RevizyonIstendi`).

### 1.3 Ölçü ve Birim Kuralları
- Desteklenen birimler: `cm`, `m`, `in`, `ft`. İç temsil **her zaman cm (decimal, 1 ondalık)**.
- Dönüşüm: 1 in = 2,54 cm; 1 ft = 30,48 cm; 1 m = 100 cm. Hem `,` hem `.` ondalık ayırıcı kabul edilsin ("3,00" = 300 cm).
- Doğrulama: min 10 cm (her kenar), maks genişlik 2.000 cm, maks yükseklik = malzemenin MaxHeightCm'i. Hatalar alan altında Türkçe gösterilsin.
- Birim değişince girilen değerler dönüştürülerek korunur (sıfırlanmaz).

### 1.4 Fiyatlama Motoru (`IPricingService`) – **tek doğruluk kaynağı sunucudur**
```
uretimEn   = en + bleed          (bleed = material.BleedCm, varsayılan 5)
uretimBoy  = boy + bleed
panelSayisi = ceil(uretimEn / material.PanelWidthCm)
alanM2      = (en * boy) / 10_000                 // müşteriye gösterilen alan
faturaM2    = max(uretimEn * uretimBoy / 10_000, material.MinBillableAreaM2)
satirTutari = round(faturaM2 * material.PricePerM2, 2) * adet
```
- Payın müşteriye mi yansıtılacağı `PricingOptions.ChargeBleed` ayarıyla seçilebilsin (varsayılan: true, arayüzde açıkça "kesim payı dahil" yazsın).
- Sepet toplamı sırası: ara toplam → kupon indirimi → tutkal kuralı → kargo kuralı → KDV (dahil fiyat, KDV oranı ayarlanabilir, %20) → genel toplam.
- Yanıt DTO'su kırılım döndürsün: alanM2, faturaM2, panelSayisi, panelGenisligi, birimFiyat, araToplam, indirim, kargo, tutkal, kdv, toplam, `ucretsizKargoIcinKalan`.
- Ön yüz aynı formülü **anlık gösterim** için kullanabilir ama sepete eklerken fiyat mutlaka sunucuda yeniden hesaplanır; istemciden gelen fiyat asla kullanılmaz.
- Birim testleri: sınır değerler (10 cm, panel eşiği tam sınırında, min fatura alanı), inç/ft dönüşümü, yuvarlama, kupon+eşik kombinasyonları.

### 1.5 Konfigüratör Arayüzü (ürün sayfası)
Tek sayfada, sol tarafta canlı önizleme, sağda adım adım form:
1. **Ölçü**: birim seçici (cm/m/inç/ft) + En + Boy; altında canlı "Hesaplanan alan: X,XX m²" ve "Kesim/montaj için her kenara 5 cm ekliyoruz" bilgisi.
2. **Malzeme**: kart listesi – ad, ₺/m², gr/m², panel eni, yangın sınıfı, yapışkanlı mı, "Tutkal gerekir" rozeti. Seçili malzemeye göre fiyat anında güncellenir.
3. **Görsel ayarları**:
   - *Görseli ölçüye uydur*: **Kırp** (en-boy oranı kilitli Cropper.js alanı, kullanıcı sürükler) / **Esnet** (tüm görsel, oran bozulur – uyarı göster).
   - **Ayna** (yatay çevir), **Siyah-Beyaz**, (opsiyonel) Sepya.
   - Önizleme üzerinde **panel çizgileri** kesikli çizgiyle gösterilsin (panel sayısı kadar).
   - **Çözünürlük uyarısı**: kırpılan alanın piksel sayısı / hedef cm → efektif DPI < eşik (örn. 72) ise "Baskı kalitesi düşebilir" uyarısı.
4. **Adet** ve **Sepete Ekle**; yanında "Numune sipariş et" ve "Tasarım değişikliği iste" butonları.
5. Fiyat özeti kutusu: faturalanan m², panel sayısı, toplam, ücretsiz kargoya kalan tutar, tahmini teslim tarihi aralığı.

**URL durum senkronizasyonu**: tüm seçimler query string'e yazılsın (`history.replaceState`), sayfa bu parametrelerle açıldığında aynı durum geri yüklensin:
`?view=customizer&material=textured&unit=cm&w_cm=400.0&h_cm=200.0&fit=crop&mirror=1&filter=grayscale&crop=0.1,0.05,0.8,0.9`
Parametreler sunucu tarafında da doğrulansın (geçersizse varsayılana düş). "Bağlantıyı kopyala" butonu olsun.

### 1.6 Poster Listesi ve Seçilen Ürünün Duvarda Gösterimi

#### 1.6.1 Poster listeleme (katalog sayfası `/duvar-kagitlari`)
- Sitedeki tüm aktif posterler grid halinde listelenir: `ThumbUrl` + LQIP bulanık yer tutucu, `loading="lazy"`, `srcset`.
- Her kartta: başlık, "₺X/m²'den başlayan" fiyat (en ucuz aktif malzeme), renk noktaları (DominantColors), ♡ Favori, **"Duvarımda Dene"** (listeye ekle) ve **"Duvarında Gör"** düğmeleri.
- Kart üzerine gelince (mobilde dokununca) görsel, varsayılan oda sahnesindeki duvar görünümüne geçer (sunucuda önceden üretilmiş `SceneThumbUrl`).
- Filtreler: etiket grupları (renk, oda, stil, tema, ton, yoğunluk, ölçek), ProductType (Mural/Pattern), yönelim (yatay/dikey/kare – AspectRatio'dan), renge göre arama (HEX yakınlığı). Sıralama: çok satan, yeni, fiyat. Sayfalama: sonsuz kaydırma + SEO için `?page=` bağlantıları.
- Tüm filtre ve sayfa durumu URL'de tutulur; filtre sonuçları htmx/fetch ile kısmi güncellenir (tam sayfa yenileme yok).

#### 1.6.2 "Duvarımda Dene" listesi (karşılaştırma tepsisi)
- Katalogdan veya ürün sayfasından eklenen posterler ekranın altında sabit bir **tepside** küçük resim olarak durur (maks. 12). Sürükle-bırak ile sıralanır, tek tıkla çıkarılır.
- Misafirde çerez anahtarıyla veritabanında, üyede hesaba bağlı tutulur; girişte birleştirilir. Liste paylaşılabilir bağlantı üretir (`/duvarimda-dene/{shareToken}`).
- "Karşılaştır" modu: seçili 2–4 poster, **aynı oda sahnesi ve aynı duvar ölçüsünde** yan yana gösterilir.

#### 1.6.3 Duvar görüntüleyici (Wall Visualizer – `/duvarinda-gor` – bkz. 1.6.6)
Yerleşim: ortada büyük sahne, solda/altta **poster listesi paneli**, sağda sahne ve ölçü kontrolleri.
- **Poster listesi paneli**: sekmeler → "Denediklerim" (TryOnList), "Favorilerim", "Tüm posterler" (arama + aynı filtreler, sonsuz kaydırma). Bir postere tıklanınca sahnedeki duvar **anında** o posterle değişir; ölçü, malzeme, ayna, filtre ayarları korunur. Klavye ok tuşlarıyla önceki/sonraki poster.
- **Sahne seçimi**: hazır oda sahneleri (RoomScene) küçük resim şeridi + "Kendi odamı yükle" (bkz. 1.6.4).
- **Ölçü**: kullanıcının girdiği duvar ölçüsü (1.3 kuralları) sahnedeki duvara ölçekli uygulanır: `pikselPerCm = wallQuadGenisligiPx / RealWallWidthCm`. Kullanıcı ölçüsü sahne duvarından küçükse poster duvarın ortasına (veya seçilen hizaya: sol/orta/sağ) yerleşir, kalan duvar görünür; büyükse sahne duvarı ölçüsüne kırpılır ve uyarı verilir.
- **Mural** ürünlerde kırp/esnet + kırpma alanı uygulanır; **Pattern** ürünlerde desen RepeatWidthCm/RepeatHeightCm ölçeğinde döşenir (Straight veya HalfDrop kaydırmalı).
- **Render katmanları** (istemcide WebGL, desteklenmezse Canvas 2D):
  1. Oda taban görseli
  2. Poster → duvar dörtgenine **perspektif (homografi) dönüşümü**
  3. ShadowMap ile **multiply** karışımı (ışık/gölge korunur)
  4. ForegroundMask (mobilya/bitki) en üstte → poster koltuğun, lambanın **arkasında** görünür
  5. Opsiyonel panel ek yeri çizgileri (kesikli, aç/kapa)
- Ayna, siyah-beyaz, malzeme dokusu (dokulu/hasır malzeme için hafif doku overlay) önizlemede de uygulanır.
- Yakınlaştır/kaydır, tam ekran, **"Görseli indir"** (filigranlı JPG) ve **"Bağlantıyı paylaş"**.
- Durum URL'de: `?product={slug}&scene={id}&w_cm=&h_cm=&material=&fit=&mirror=&filter=&align=center&crop=`.
- Görüntüleyiciden doğrudan **"Bu tasarımla sepete ekle"** → konfigürasyon aynen sepete aktarılır; fiyat 1.4'e göre sunucuda hesaplanır ve panelde canlı gösterilir.
- Ürün sayfasındaki konfigüratörde de önizleme alanının üstünde **"Düz görünüm / Duvarında Gör"** geçişi bulunur; aynı görüntüleyici bileşeni kullanılır (tek JS modülü: `wall-visualizer.js`, tek sunucu servisi: `IWallPreviewRenderer`).

#### 1.6.4 Kendi oda fotoğrafında önizleme
- Kullanıcı oda fotoğrafı yükler (JPG/PNG/WebP, max 10 MB, sihirli bayt kontrolü, EXIF temizliği, yeniden kodlama).
- Duvarın **4 köşesini** sürükleyerek işaretler; tasarım perspektif dönüşümü ile duvara yerleştirilir (istemcide canvas/WebGL ile anlık; sunucuda ImageSharp ile yüksek kaliteli sonuç kaydı). Çarpma (multiply) karışımı ile gölgeler korunsun.
- Üye başına ücretsiz kota (varsayılan 10) – `RateLimiter` + veritabanı sayacı. Misafire giriş yapmasını öner.
- Kullanıcı ayrıca duvarın gerçek genişliğini (cm) girer; ölçek bu değerden hesaplanır. Önündeki mobilyayı maskelemek için basit "fırça ile maske boya" aracı (opsiyonel).
- Yüklenen oda, kullanıcının **özel sahnesi** olarak kaydedilir ve hazır sahneler gibi poster listesiyle birlikte kullanılır (her poster değişimi kotadan düşmez; kota yalnızca yüksek kaliteli sunucu render'ı / indirme için sayılır).
- Arayüz bir `IWallPreviewRenderer` arayüzü üzerinden çalışsın; ileride AI tabanlı duvar/mobilya segmentasyonu (otomatik köşe ve maske tespiti) eklenebilecek şekilde.

#### 1.6.5 Sunucu tarafı mockup üretimi ve önbellek
- Yeni poster eklendiğinde veya sahne değiştiğinde `BackgroundService` her poster × varsayılan sahne için `SceneThumbUrl` (800px) üretir; diğer sahneler ilk istekte üretilip önbelleğe alınır.
- Önbellek anahtarı: `productId + sceneId + w_cm + h_cm + fit + mirror + filter + crop` hash'i; dosya depolamada + `HybridCache`. Poster veya sahne güncellenince ilgili anahtarlar geçersiz kılınır.
- İstemci render'ı ile sunucu render'ı aynı homografi ve katman sırasını kullanmalı (piksel farkı testle doğrulanır).

#### 1.6.6 Mevcut siteye "Duvarında Gör" bağlantısı (entegrasyon güncellemesi)
Amaç: mevcut sitedeki **her poster/duvar kağıdı ürününe** "Duvarında Gör" bağlantısı eklemek ve bu bağlantının, mevcut sayfa yapısını bozmadan duvar görüntüleyiciyi seçili ürünle açması.

**A) Bağlantının eklenecekleri yerler**
- Ürün listesi / kategori kartları (Duvarımda Dene ve Favori'nin yanında).
- Ürün detay sayfası: ana görselin altında ve "Sepete Ekle" düğmesinin yanında belirgin ikincil düğme (ikon: duvar/oda).
- Konfigüratör önizleme alanı ("Düz görünüm / Duvarında Gör" geçişi).
- Sepet satırları ve sipariş onay e-postası ("Siparişini duvarında gör").
- Ortak **Razor View Component / Tag Helper** ile üretilsin, tüm sayfalarda tek yerden yönetilsin:
  ```cshtml
  <wall-preview-link product="@Model.Slug" config="@Model.CurrentConfig" variant="button|icon|text" />
  ```
  Çıktı: `<a href="/duvarinda-gor?product=...&w_cm=...&material=..." data-wall-preview="..." class="btn-wall-preview">Duvarında Gör</a>`

**B) Bağlantı (deep link) sözleşmesi**
- Rota: `GET /duvarinda-gor` (Razor Page) ve kısa yol `GET /p/{slug}/duvarinda-gor`.
- Parametreler: `product` (zorunlu, slug), `scene`, `w_cm`, `h_cm`, `unit`, `material`, `fit`, `mirror`, `filter`, `crop`, `align`, `return` (geri dönülecek sayfa, yalnızca aynı site içi göreli yol – open redirect koruması).
- Ürün sayfasında kullanıcı ölçü/malzeme seçtiyse bağlantı **o anki konfigürasyonla** açılır (JS, bağlantının `href`'ini konfigürasyon değiştikçe günceller). Seçim yoksa varsayılan sahne + ürünün en-boy oranına uygun varsayılan ölçü (örn. 300 × 250 cm) kullanılır.
- Geçersiz/pasif ürün → 404 yerine katalog önerileriyle "Ürün bulunamadı" sayfası; geçersiz parametreler sessizce varsayılana düşer.

**C) Açılış davranışı (progressive enhancement)**
- JS yoksa: bağlantı tam sayfa `/duvarinda-gor` açar (SEO/erişilebilirlik için gerçek `<a href>`).
- JS varsa: `wall-preview-link.js` tıklamayı yakalar ve görüntüleyiciyi **tam ekran modal** içinde açar (`<dialog>`), URL'yi `history.pushState` ile günceller; tarayıcı geri tuşu modalı kapatır. Mobilde modal yerine tam sayfa.
- Modal içinde görüntüleyicinin tüm özellikleri (1.6.3) çalışır: sahne değiştirme, poster listesinden değiştirme, ölçü/malzeme, kendi odanı yükle.
- Modal kapatılınca veya "Bu ayarlarla devam et" denince, görüntüleyicide yapılan değişiklikler (ölçü, malzeme, kırpma, ayna, filtre) ürün sayfasındaki konfigüratöre **geri aktarılır** (aynı sayfa ise JS olayı `wallpreview:apply`, farklı sayfa ise `return` URL'sine query string ile).
- Görüntüleyici görselleri ve sahne verisi bağlantıya ilk hover/focus'ta **ön yüklenir** (`<link rel="prefetch">`), açılış gecikmesi düşük olsun.

**D) Başka platformdaki mevcut site için gömülebilir widget (site ASP.NET dışındaysa – WooCommerce, Shopify vb.)**
- Tek satırlık script ile eklenebilsin:
  ```html
  <script src="https://{alanadi}/embed/duvarinda-gor.js" data-api-key="PUBLIC_KEY" defer></script>
  <a data-duvarinda-gor data-product="klasik-orman-manzarali" data-image="https://.../poster.jpg">Duvarında Gör</a>
  ```
- Script sayfadaki `[data-duvarinda-gor]` öğelerini bulur (yoksa `data-auto-inject="selector"` ile belirtilen yere düğmeyi kendisi ekler), tıklanınca `/embed/duvarinda-gor?...` adresini **iframe modal** içinde açar.
- Ürün bu sistemde kayıtlı değilse `data-image` (+ `data-width-cm`, `data-height-cm`, `data-type=mural|pattern`) ile harici görsel kabul edilir; sunucu görseli yalnızca **izinli alan adlarından** indirir (SSRF koruması: allowlist, özel IP engeli, boyut/zaman aşımı sınırı), önbelleğe alır ve türevlerini üretir.
- Güvenlik: `EmbedClient` tablosu (PublicKey, AllowedOrigins, IsActive, günlük kota). `/embed/*` yanıtlarında `Content-Security-Policy: frame-ancestors {izinli originler}`; diğer sayfalarda `frame-ancestors 'self'`. CORS yalnızca izinli originler için.
- İframe ↔ ana sayfa iletişimi `postMessage` ile, origin doğrulamalı: `wallpreview:ready`, `wallpreview:resize`, `wallpreview:apply` (seçilen konfigürasyon), `wallpreview:add-to-cart` (ana sitenin kendi sepetine eklemesi için ürün + ölçü + malzeme bilgisini iletir), `wallpreview:close`.

**E) Ölçüm**
- Olaylar: `wall_preview_link_click` (kaynak: kart/detay/sepet/e-posta/embed), `wall_preview_scene_change`, `wall_preview_product_swap`, `wall_preview_add_to_cart`. Admin panelinde "Duvarında Gör → sepete ekleme dönüşüm oranı" raporu.

**F) Özellik bayrağı ve geriye uyumluluk**
- `FeatureManagement` ile `WallPreviewLink` bayrağı: kapalıyken bağlantı hiç render edilmez; kademeli açma (yüzde bazlı) desteklensin.
- Mevcut ürün sayfası şablonlarında yalnızca Tag Helper satırı eklenir; mevcut rotalar, sepet ve sipariş akışı değişmez. Mevcut ürünler için eksik türev görseller ve `SceneThumbUrl`'ler tek seferlik **geri doldurma (backfill) işi** ile üretilir (`dotnet run -- backfill-wall-previews`, kaldığı yerden devam edebilir).

### 1.7 Sipariş Sonrası Akış
- Sipariş oluşunca her kalem için **üretim dosyası** arka planda oluşturulsun (`BackgroundService` + kanal/kuyruk): kırpma → esnet/kırp → ayna → filtre → hedef ölçü + pay, 150 DPI hedefi, panellere bölünmüş PDF/TIFF + her panelde numara ve hizalama işareti.
- Müşteriye **ölçüsüne uyarlanmış onay önizlemesi** e-postası gitsin (`ProductionProof`). Müşteri "Onayla" veya "Revizyon iste" diyebilsin; onaysız sipariş üretime geçmesin (ayar ile otomatik onay süresi, örn. 24 saat).
- **Teslim tarihi hesabı** (`IDeliveryEstimator`): üretim 1–2 iş günü + kargo 1–3 iş günü; hafta sonu ve Türkiye resmi tatilleri iş günü sayılmaz; saat 14:00 sonrası siparişler ertesi iş gününden başlar. Ürün sayfasında "Tahmini teslim: 2–4 Ekim" gibi gösterilsin.
- Sipariş durumları: `Olusturuldu → OdemeAlindi → OnayBekliyor → Uretimde → Kargoda → TeslimEdildi` (+ `Iptal`, `Iade`).

### 1.8 Ek Modüller
- **Numune siparişi**: malzeme başına küçük numune (örn. A4), sabit fiyat (örn. 200 ₺), stoklu; aynı sepette konfigüre ürünle birlikte olabilir.
- **Tutkal upsell**: `RequiresGlue` olan malzeme seçildiyse sepete tutkal eklemeyi öner; sepet ≥ 1.000 ₺ ise tutkal ücretsiz.
- **Tasarım değişiklik talebi**: form + dosya ekleri, admin'de liste/durum yönetimi, müşteriye ve admin'e e-posta, SLA takibi (2 iş günü).
- **Kupon**: sepette uygulanır, kurallar sunucuda doğrulanır.
- **Katalog ve favoriler**: ayrıntılar 1.6.1–1.6.2'de.

### 1.9 API Uç Noktaları (Minimal API, `/api/v1`)
- `GET  /materials` – aktif malzemeler ve özellikleri
- `POST /pricing/quote` – konfigürasyon → fiyat kırılımı (rate limit'li)
- `POST /cart/items` – konfigüre ürün ekle (fiyatı yeniden hesaplar)
- `PATCH/DELETE /cart/items/{id}`, `POST /cart/coupon`
- `GET  /products?tags=&type=&orientation=&color=&sort=&page=` – poster listesi (kart DTO: slug, başlık, thumb, lqip, sceneThumb, başlangıç fiyatı, renkler)
- `GET  /products/{slug}` – detay + görüntüleyici için PreviewUrl, AspectRatio, ProductType, repeat bilgisi
- `GET  /scenes` – aktif hazır oda sahneleri (taban, gölge, maske URL'leri, WallQuad, gerçek duvar ölçüsü)
- `GET  /scenes/{sceneId}/render?product=&w_cm=&h_cm=&material=&fit=&mirror=&filter=&crop=&align=&size=` – sunucu render'ı (önbellekli JPG/WebP)
- `GET/POST/DELETE /try-on-list/items`, `PATCH /try-on-list/order`, `POST /try-on-list/share`
- `GET/POST/DELETE /favorites`
- `GET  /wall-preview/link?product=&...` – normalize edilmiş "Duvarında Gör" bağlantısı üretir (e-posta ve harici kullanım için)
- `GET  /embed/duvarinda-gor.js`, `GET /embed/duvarinda-gor` (iframe sayfası), `POST /embed/external-images` (izinli alan adından harici görsel kaydı; PublicKey + Origin doğrulamalı)
- `POST /events/wall-preview` – ölçüm olayları
- `POST /room-previews` (multipart), `GET /room-previews/quota`
- `POST /design-requests` (multipart)
- `GET  /delivery/estimate?materialCode=`
- `POST /orders/{id}/proofs/{proofId}/approve|reject`
Tümünde `ProblemDetails`, FluentValidation, OpenAPI (`Microsoft.AspNetCore.OpenApi` + Scalar UI).

### 1.10 Admin Paneli (Razor Pages, `[Authorize(Roles="Admin")]`)
Ürün/etiket CRUD + görsel yükleme, **toplu poster yükleme** (çoklu dosya veya ZIP + CSV: başlık, slug, etiketler, ProductType, repeat ölçüleri; yüklemede otomatik türev görsel, LQIP, baskın renk çıkarımı, çözünürlük/DPI kontrolü ve hatalı satır raporu), liste sırası ve öne çıkarma, **oda sahnesi yönetimi** (taban/gölge/maske görseli yükleme, duvarın 4 köşesini görsel üzerinde sürükleyerek tanımlama, gerçek duvar ölçüsü girme, canlı test render'ı, varsayılan sahne seçme, mockup önbelleğini yeniden üretme), **"Duvarında Gör" ayarları** (özellik bayrağı, varsayılan sahne ve ölçü, embed istemcileri ve izinli originler, dönüşüm raporu), malzeme ve fiyat yönetimi, kargo/tutkal/KDV/pay ayarları, kupon yönetimi, siparişler ve üretim dosyası indirme, onay önizlemeleri, tasarım talepleri, oda önizleme kota ayarı.

### 1.11 Güvenlik, Performans, SEO
- Dosya yüklemede: uzantı + MIME + sihirli bayt kontrolü, boyut limiti, yeniden kodlama, rastgele dosya adı, web kökü dışında depolama (`IFileStorage`: yerel disk / S3 uyumlu).
- Anti-forgery, rate limiting (`AddRateLimiter`), çıktı önbelleği (`OutputCache`) katalog sayfaları için.
- Önizleme görselleri için düşük çözünürlüklü, filigranlı türevler; orijinal yüksek çözünürlük asla herkese açık URL'de olmasın.
- Ürün sayfalarında `schema.org/Product` JSON-LD, canonical URL (konfigüratör parametreleri canonical'a dahil edilmesin), Open Graph.
- Erişilebilirlik: form etiketleri, klavye ile kırpma/köşe ayarlama, renk kontrastı.

### 1.12 Testler
- xUnit + FluentAssertions: fiyatlama, birim dönüşümü, teslim tarihi (tatil/hafta sonu), kupon kuralları.
- `WebApplicationFactory` ile entegrasyon testleri: quote → sepete ekle → sipariş.
- Görsel işleme için örnek görselle snapshot testi (boyut, ayna, gri tonlama doğrulaması).
- Duvar görüntüleyici: homografi matematiği (köşe noktalarının doğru eşlenmesi), cm→piksel ölçeği, Pattern döşeme (Straight/HalfDrop), maske katman sırası, render önbellek anahtarı ve geçersiz kılma.
- Poster listesi: filtre/sıralama/sayfalama sorguları, TryOnList maks. öğe sınırı, misafir → üye liste birleştirme, paylaşım bağlantısı.
- Playwright ile uçtan uca: katalogdan "Duvarımda Dene" → görüntüleyicide listeden poster değiştir → ölçü gir → sepete ekle.
- "Duvarında Gör": Tag Helper çıktısı (href parametreleri), JS'siz tam sayfa açılış, modal açma/kapama + geri tuşu, konfigürasyonun ürün sayfasına geri aktarımı, `return` parametresinde open redirect engeli, özellik bayrağı kapalıyken bağlantının görünmemesi.
- Embed: izinsiz origin'de iframe'in yüklenmemesi (frame-ancestors), postMessage origin doğrulaması, harici görselde SSRF engeli (özel IP, izinsiz alan adı, aşırı boyut).

### 1.13 Teslim Beklentisi
1. Önce solution yapısını ve domain modelini çıkar, onayımı bekle.
2. Ardından sırasıyla: Domain + EF Core → PricingService + testler → API → poster listeleme + favoriler + "Duvarımda Dene" listesi → konfigüratör sayfası → duvar görüntüleyici (hazır sahneler) → **"Duvarında Gör" bağlantısı (Tag Helper, modal, mevcut sayfalara ekleme, backfill işi)** → kendi oda fotoğrafı → gömülebilir widget → sipariş/üretim akışı → admin (toplu poster yükleme + sahne yönetimi dahil).
3. Her adımda çalıştırılabilir kod, migration ve kısa README (kurulum, seed, ortam değişkenleri) ver.
4. Varsayım yaptığın her yeri kodda `// VARSAYIM:` yorumu ile işaretle.
