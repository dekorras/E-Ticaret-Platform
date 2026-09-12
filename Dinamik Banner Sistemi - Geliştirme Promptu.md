# Dinamik Banner / Sayfa Bloğu Sistemi — Geliştirme Promptu

> Aşağıdaki metni bir kod üreten yapay zekâya olduğu gibi verebilirsiniz. `[KÖŞELİ PARANTEZ]` içindeki yerleri kendi projenize göre doldurun.

* * *

## 1\. Rol ve Bağlam

Sen kıdemli bir **ASP.NET Core 9** geliştiricisisin. Aynı zamanda Bootstrap 5.3 grid sistemi, modern CSS (container queries, `aspect-ratio`, custom properties) ve Entity Framework Core ile hiyerarşik veri modelleme konularında uzmansın.

Mevcut projem:

- **Framework:** ASP.NET Core 9 MVC (Razor View Engine)
- **ORM:** Entity Framework Core 9, Code First, Migrations
- **Veritabanı:** Microsoft SQL Server
- **Ön yüz:** Bootstrap 5.3, vanilla JavaScript (jQuery mevcut/\[VARSA BELİRT\])
- **Admin panel:** `Areas/Admin` altında MVC, mevcut authorization altyapısı: `[ÖRN: ASP.NET Core Identity, "AdminPolicy"]`
- **Proje adı / namespace:** `[PROJE.NAMESPACE]`
- **Mevcut katmanlar:** `[ÖRN: Core / Data / Service / Web]`

## 2\. Amaç

Anasayfada (ve istenirse başka sayfalarda) **tamamen veritabanı üzerinden yönetilen, iç içe geçebilen bir banner/blok düzeni** kurmak istiyorum. Referans: `dekorras.com` anasayfasında olduğu gibi üst üste sıralanmış, her biri farklı sayıda kolona bölünmüş, kolonların içinde de alt satırların bulunabildiği görsel banner blokları.

Yönetici panelinden:

- İstediği kadar **satır (row)** ekleyebilmeli,
- Her satırı **istediği kadar kolona** bölebilmeli,
- Her kolonu tekrar **satırlara**, o satırları tekrar kolonlara bölebilmeli (**sınırsız/derinliği sınırlı iç içe yapı**),
- Her kolona içerik (görsel, başlık, alt başlık, metin, buton, link, video, HTML) atayabilmeli,
- Kolon genişliklerini **her breakpoint için ayrı ayrı** (xs/sm/md/lg/xl/xxl) belirleyebilmeli,
- Tüm bunları **sürükle\-bırak görsel bir editörle**, canlı önizleme eşliğinde yapabilmeli.

Kodu yazarken hiçbir adımı "burayı sen tamamlarsın" diye boş bırakma; çalışır, uçtan uca bir çözüm üret.

* * *

## 3\. Veri Modeli

Aşağıdaki modeli kur. Tek bir **kendine referans veren (self\-referencing) ağaç** kullan — ayrı Row/Column tabloları **kullanma**, çünkü iç içe geçme derinliği önceden bilinemez.

### 3\.1 `BannerZone` (Bölge / Yerleşim Alanı)

Bir sayfanın belirli bir bölgesini temsil eder (örn. "Anasayfa \- Orta Alan", "Kategori Sayfası \- Üst").

| Alan | Tip | Açıklama |
| --- | --- | --- |
| `Id` | int | PK |
| `Key` | string(100) | Benzersiz, kodda çağrılacak anahtar (`home-main`) |
| `Name` | string(200) | Panelde görünen ad |
| `Description` | string(500)? | Açıklama |
| `IsActive` | bool | Yayında mı |
| `CultureCode` | string(10)? | Çok dillilik için (`tr-TR`), null \= tüm diller |
| `CreatedAt` / `UpdatedAt` | DateTime | Denetim |

### 3\.2 `BannerNode` (Satır / Kolon Düğümü — özyinelemeli)

| Alan | Tip | Açıklama |
| --- | --- | --- |
| `Id` | int | PK |
| `BannerZoneId` | int | FK → BannerZone |
| `ParentId` | int? | FK → BannerNode (null \= kök seviye) |
| `NodeType` | enum | `Row` \| `Column` |
| `SortOrder` | int | Kardeşler arası sıra |
| `Depth` | int | Kök \= 0 (hesaplanır, doğrulama için) |
| `Path` | string(400) | Materialized path (`/1/5/12/`) — alt ağaç sorguları ve döngü kontrolü için |
| `IsActive` | bool |  |
| `CustomCssClass` | string(300)? | Ek sınıflar |
| `CustomId` | string(100)? | HTML `id` |
| `SettingsJson` | nvarchar(max) | Aşağıdaki ayar nesnesi (JSON) |

**Kural:** `Row` düğümünün çocukları yalnızca `Column`, `Column` düğümünün çocukları yalnızca `Row` olabilir. Bunu hem veri erişim katmanında hem admin API'sinde doğrula ve ihlalde anlamlı hata döndür.

### 3\.3 `BannerContent` (İçerik Bloğu)

Bir `Column` düğümüne bağlanır. Bir kolonda birden fazla içerik olabilir (sıralı).

| Alan | Tip | Açıklama |
| --- | --- | --- |
| `Id` | int | PK |
| `BannerNodeId` | int | FK → BannerNode (yalnızca `Column` tipli) |
| `ContentType` | enum | `Image`, `ImageWithOverlay`, `Heading`, `Text`, `Button`, `LinkList`, `Video`, `RawHtml`, `Spacer`, `ProductWidget` |
| `SortOrder` | int |  |
| `Title` / `Subtitle` | string(300)? |  |
| `Body` | nvarchar(max)? | Zengin metin (sanitize edilir) |
| `ImagePath` | string(500)? | Ana görsel |
| `ImagePathMobile` | string(500)? | Mobil için ayrı görsel (art direction) |
| `AltText` | string(300)? | Erişilebilirlik — zorunlu tut |
| `LinkUrl` | string(500)? |  |
| `LinkTarget` | string(20)? | `_self` / `_blank` |
| `ButtonText` | string(100)? |  |
| `SettingsJson` | nvarchar(max) | İçeriğe özel ayarlar |
| `StartDate` / `EndDate` | DateTime? | Zamanlanmış yayın |
| `IsActive` | bool |  |

### 3\.4 `SettingsJson` Şeması (Node)

Ayarları ayrı kolonlara dağıtmak yerine tek bir tipli JSON nesnesinde tut; C\# tarafında POCO'ya deserialize et (`System.Text.Json`, `JsonSerializerOptions` ile camelCase).

```jsonc
{
  // Yalnızca Column düğümleri için — Bootstrap grid genişlikleri (1-12, "auto", null=devral)
  "cols":    { "xs": 12, "sm": 12, "md": 6, "lg": 4, "xl": 3, "xxl": 3 },
  "offset":  { "md": 0, "lg": 1 },
  "order":   { "xs": 2, "md": 1 },
  "hidden":  { "xs": false, "md": false, "lg": false },   // d-none / d-*-block

  // Yalnızca Row düğümleri için
  "gutterX": 3,               // g-x-*  (0-5)
  "gutterY": 3,               // g-y-*
  "alignItems": "center",     // start | center | end | stretch  → align-items-*
  "justifyContent": "start",  // start | center | end | between | around | evenly
  "fullWidth": false,         // container-fluid mı, container mı
  "reverseOnMobile": false,   // flex-column-reverse flex-md-row

  // Her iki tip için ortak görünüm ayarları (CSS custom property'ye çevrilir)
  "style": {
    "backgroundColor": "#ffffff",
    "backgroundImage": "/uploads/bg.webp",
    "backgroundSize": "cover",
    "backgroundPosition": "center",
    "backgroundAttachment": "scroll",
    "overlayColor": "rgba(0,0,0,.35)",
    "minHeight":   { "xs": "220px", "md": "380px", "lg": "460px" },
    "paddingY":    { "xs": "1rem", "md": "3rem" },
    "paddingX":    { "xs": ".75rem", "md": "1.5rem" },
    "marginY":     { "xs": "0", "md": "1rem" },
    "borderRadius": "12px",
    "textAlign":   { "xs": "center", "md": "left" },
    "textColor": "#111827"
  },

  "animation": { "type": "fade-up", "delay": 100 },   // opsiyonel, prefers-reduced-motion'a saygılı
  "aspectRatio": "16/9"                               // CLS önlemek için
}
```

* * *

## 4\. Veri Erişim ve Servis Katmanı

1. `ApplicationDbContext`'e `DbSet`'leri ekle; `IEntityTypeConfiguration<T>` sınıflarıyla yapılandır.
2. `BannerNode` self\-referencing ilişkisinde **`DeleteBehavior.Restrict`** kullan (SQL Server çoklu cascade path hatasını önlemek için) ve silmeyi servis katmanında özyinelemeli olarak (ya da `Path` LIKE sorgusu ile toplu) yönet.
3. Sık kullanılan indeksler: `(BannerZoneId, ParentId, SortOrder)`, `Path` üzerinde index, `BannerZone.Key` üzerinde unique index.
4. `IBannerService` arayüzü ve implementasyonu:
   - `Task<BannerZoneDto?> GetRenderTreeAsync(string zoneKey, string? culture, CancellationToken ct)` — **tek sorguda** tüm düğüm \+ içerikleri çek (`AsNoTracking`, `AsSplitQuery`), bellekte ağaca dönüştür (N\+1 yok, özyinelemeli DB çağrısı yok).
   - Yayın tarihi/aktiflik filtrelerini uygula.
   - `IMemoryCache` (veya `HybridCache`) ile `banner-zone:{key}:{culture}` anahtarıyla cache'le; herhangi bir düzenlemede ilgili anahtarı invalidate et.
   - Admin CRUD: `CreateNodeAsync`, `MoveNodeAsync(nodeId, newParentId, newIndex)`, `ReorderAsync`, `DuplicateNodeAsync` (alt ağacıyla birlikte), `DeleteSubtreeAsync`, `SaveLayoutAsync(zoneId, LayoutDto)`.
   - `MoveNodeAsync` bir düğümün kendi alt ağacına taşınmasını engellemeli (`Path` kontrolü).
5. `AutoMapper` veya elle yazılmış mapper ile Entity ↔ DTO dönüşümü. Razor view'lara **entity değil DTO** ver.

* * *

## 5\. Ön Yüz Render Katmanı

1. `BannerZoneViewComponent` yaz: `@await Component.InvokeAsync("BannerZone", new { key = "home-main" })`.
2. Özyinelemeli render için `Views/Shared/Components/BannerZone/_Node.cshtml` partial'ı kullan — partial kendini çağırsın (`RecursionDepth` sınırı: maks. 6 seviye, aşılırsa render'ı kes ve log'la).
3. **Grid sınıf üretimi:** `SettingsJson.cols` değerlerinden Bootstrap sınıflarını üret (`col-12 col-md-6 col-lg-4 offset-lg-1 order-md-1`). Bunu bir `BannerCssBuilder` yardımcı sınıfında topla, view içinde string birleştirme yapma.
4. **Inline stiller yerine CSS custom property** kullan. Her düğüme yalnızca değişkenleri bas, görsel kuralları site CSS'inde tanımla:

```html
<div class="bnr-row row g-md-4 align-items-center"
     style="--bnr-bg:#fff; --bnr-min-h:220px; --bnr-min-h-md:380px; --bnr-pad-y:1rem; --bnr-pad-y-md:3rem;">
```

5. **Görseller:** `<picture>` \+ `srcset` \+ `sizes`, `loading="lazy"` (ilk ekran/LCP görselinde `loading="eager"` \+ `fetchpriority="high"`), `decoding="async"`, `width`/`height` veya `aspect-ratio` ile CLS sıfır.
6. **Güvenlik:** `RawHtml` ve `Body` alanlarını render etmeden önce **Ganss.Xss (HtmlSanitizer)** ile temizle; `Html.Raw` çağrısını yalnızca sanitize edilmiş içerik için kullan.
7. Çıktı önbelleği: .NET 9 **Output Caching** ile zone bazlı `[OutputCache(Tags = ["banner-home-main"])]`, düzenlemede `IOutputCacheStore.EvictByTagAsync`.

* * *

## 6\. CSS Yapılandırması (Tüm Cihazlar)

Ayrı bir `wwwroot/css/banner-builder.css` dosyası oluştur ve şunları içersin:

1. **Bootstrap 5.3 grid'i temel al**, üzerine yaz — grid'i sıfırdan yazma.
2. Breakpoint'ler Bootstrap ile aynı olsun: `sm 576px`, `md 768px`, `lg 992px`, `xl 1200px`, `xxl 1400px`.
3. Custom property'lerin breakpoint bazlı devri:

```css
.bnr-node {
  --bnr-min-h-current: var(--bnr-min-h, auto);
  --bnr-pad-y-current: var(--bnr-pad-y, 0);
  min-height: var(--bnr-min-h-current);
  padding-block: var(--bnr-pad-y-current);
  padding-inline: var(--bnr-pad-x, 0);
  background-color: var(--bnr-bg, transparent);
  border-radius: var(--bnr-radius, 0);
  color: var(--bnr-text-color, inherit);
}
@media (min-width: 768px) {
  .bnr-node {
    --bnr-min-h-current: var(--bnr-min-h-md, var(--bnr-min-h, auto));
    --bnr-pad-y-current: var(--bnr-pad-y-md, var(--bnr-pad-y, 0));
  }
}
/* lg, xl, xxl için aynı kalıbı tekrarla */
```

4. **Arka plan görseli \+ overlay:** `::before` pseudo\-element ile overlay, `background-image: var(--bnr-bg-img)`; mobilde `background-attachment: fixed` kullanma (iOS'ta bozuk).
5. **Container queries:** İç içe kolonlarda banner içeriği viewport'a değil **kapsayıcı kolonun genişliğine** göre davranmalı. `.bnr-col { container-type: inline-size; }` ve içerik için `@container (min-width: 400px) { ... }`. Destekleyen tarayıcılarda tipografi/padding container'a göre; `@supports not (container-type: inline-size)` fallback'i media query ile ver.
6. **Akıcı tipografi:** `clamp()` ile başlık boyutları — `font-size: clamp(1.25rem, 2.5cqi + .5rem, 2.5rem)`.
7. **Görsel oranı:** `.bnr-media { aspect-ratio: var(--bnr-ar, 16/9); } .bnr-media img { width:100%; height:100%; object-fit: cover; object-position: var(--bnr-focal, center); }`
8. **Mobil sıra değişimi:** `order-*` sınıfları ve `.bnr-row--reverse-mobile { flex-direction: column-reverse; } @media (min-width:768px){ flex-direction: row; }`
9. **Gizleme:** breakpoint bazlı `d-none d-md-block` üretimi (`hidden` ayarından).
10. **Erişilebilirlik:** odak halkaları (`:focus-visible`), en az 4.5:1 kontrast uyarısı panelde, `@media (prefers-reduced-motion: reduce)` ile animasyonları kapat, dokunma hedefleri ≥ 44×44px.
11. **RTL desteği:** `margin-inline`, `padding-inline`, `inset-inline` gibi logical property'ler kullan.
12. **Karanlık mod (opsiyonel):** `@media (prefers-color-scheme: dark)` altında değişken override'ı.
13. **Print:** arka plan görsellerini gizleyen sade bir `@media print` bloğu.

* * *

## 7\. Admin Panel — Sürükle\-Bırak Görsel Editör

`Areas/Admin/Controllers/BannerBuilderController.cs` \+ ilgili view'lar.

### 7\.1 Ekran Düzeni

Üç panelli çalışma alanı:

- **Sol:** Bileşen kutusu — sürüklenip bırakılabilen "Satır", "Kolon", ve içerik tipleri (Görsel, Başlık, Metin, Buton, Link Listesi, Video, HTML, Boşluk).
- **Orta:** Canvas — gerçek Bootstrap grid ile render edilen **WYSIWYG** alan. Düğümlerin üzerine gelince gösterilen araç çubuğu: sürükle tutamacı, "kolona böl", "satır ekle", "kopyala", "ayarlar", "sil".
- **Sağ:** Seçili düğümün ayar paneli — sekmeler: `Düzen` (breakpoint bazlı kolon genişliği slider'ları), `Stil` (renk, boşluk, arka plan, köşe), `İçerik`, `Gelişmiş` (custom class/id, görünürlük, zamanlama).

### 7\.2 Zorunlu Davranışlar

1. **Sürükle\-bırak:** SortableJS (iç içe listeler, `group` \+ `pull/put` kuralları ile Row→Column, Column→Row kısıtı). Sürükleme sırasında geçerli bırakma hedefleri vurgulansın, geçersizler kırmızı olsun.
2. **Kolona bölme:** Bir satır seçiliyken "Kolona böl" düğmesi hazır şablonlar sunsun: `12`, `6+6`, `4+4+4`, `3+3+3+3`, `8+4`, `4+8`, `3+6+3`, `2+2+2+2+2+2` ve **serbest sayı girişi** (n kolon, 12'yi eşit paylaştır, kalanı ilk kolonlara dağıt).
3. **Kolonu satıra bölme:** Bir kolon seçiliyken "İçine satır ekle" — kolonun içine yeni bir `Row` düğümü açılsın, o da tekrar kolonlara bölünebilsin. Derinlik sınırı 6; sınıra gelince buton pasifleşsin ve nedeni tooltip'te yazsın.
4. **Genişlik ayarı:** Kolon kenarından **fare ile sürükleyerek** genişletme/daraltma — 12'lik grid'e snap etsin, aynı satırdaki toplam 12'yi aşarsa uyarı versin (aşmasına izin ver ama satırın kaydırılacağını bildir).
5. **Breakpoint anahtarı:** Canvas üstünde `XS / SM / MD / LG / XL / XXL` düğmeleri. Seçilene göre canvas genişliği simüle edilsin (iframe genişliği: 375 / 576 / 768 / 992 / 1200 / 1400) ve ayar paneli **o breakpoint'in** değerlerini düzenlesin. Değer girilmemiş breakpoint bir alttakinden devralsın ve panelde "devralındı" rozetiyle gösterilsin.
6. **Geri al / İleri al:** İstemci tarafında komut yığını (Ctrl\+Z / Ctrl\+Shift\+Z), en az 50 adım.
7. **Kaydetme:** Tüm düzen tek bir `POST /Admin/BannerBuilder/Save` isteğinde JSON olarak gönderilsin. Sunucu tarafında:
   - Model doğrulaması (grid kuralları, derinlik, renk/uzunluk formatları),
   - Tek transaction içinde ağacın senkronizasyonu (yeni/güncel/silinen düğümler diff'lenerek),
   - `Path` ve `Depth` alanlarının yeniden hesaplanması,
   - Cache invalidation,
   - Sonuç: `{ success, zoneId, idMap: { tempId → realId }, errors[] }`.
8. **Antiforgery token** her istekte; `[Authorize(Policy = "AdminPolicy")]`.
9. **Otomatik taslak kaydı:** 30 saniyede bir `localStorage`'a değil sunucuya `IsDraft` sürümü olarak kaydet; "Yayınla" düğmesi taslağı canlıya alsın. Sürüm geçmişi tablosu (`BannerZoneRevision`, JSON snapshot \+ tarih \+ kullanıcı) ve **geri dönme** özelliği.
10. **Canlı önizleme:** "Önizle" düğmesi gerçek site şablonunda `?previewToken=` ile taslağı gösteren yeni sekme açsın.
11. **Görsel yükleme:** Sürükle\-bırak yükleme; sunucuda uzantı \+ MIME \+ magic\-byte doğrulaması, maksimum boyut sınırı, `ImageSharp` ile WebP dönüşümü ve 3 boyut üretimi (`-sm 640w`, `-md 1024w`, `-lg 1920w`), `wwwroot/uploads/banners/{yyyy}/{MM}/` altında GUID adla saklama. Basit bir medya kütüphanesi modalı ile tekrar kullanım.
12. **Şablon kütüphanesi:** Sık kullanılan hazır blok düzenlerini "Şablon olarak kaydet" ve yeni bölgelerde tek tıkla uygulama.
13. **Erişilebilirlik uyarıları:** Alt metni boş görseller, düşük kontrastlı metin, boş link için panelde uyarı rozeti.

### 7\.3 JavaScript Mimarisi

- Tek bir ES module: `wwwroot/js/admin/banner-builder.js`, sınıf tabanlı (`BannerBuilder`, `NodeModel`, `CommandStack`, `Renderer`, `SettingsPanel`).
- Durum yönetimi: tek kaynak doğruluk (single source of truth) bir JS nesne ağacı; DOM ondan türetilir.
- Framework şart değil; ancak istersen Alpine.js kullanabilirsin — **jQuery'ye bağımlı yazma**.

* * *

## 8\. API Sözleşmesi (Admin)

```
GET    /Admin/BannerBuilder/Index?zoneKey=home-main
GET    /Admin/BannerBuilder/Tree/{zoneId}          → LayoutDto (JSON)
POST   /Admin/BannerBuilder/Save                   → LayoutDto gönderilir
POST   /Admin/BannerBuilder/Publish/{zoneId}
POST   /Admin/BannerBuilder/Media/Upload           → { url, urls: {sm,md,lg}, width, height }
GET    /Admin/BannerBuilder/Media/List?page=1
POST   /Admin/BannerBuilder/Revision/Restore/{revisionId}
DELETE /Admin/BannerBuilder/Node/{nodeId}
```

`LayoutDto` örneği:

```jsonc
{
  "zoneId": 1,
  "zoneKey": "home-main",
  "nodes": [
    {
      "id": 10, "tempId": null, "parentId": null, "type": "Row", "sortOrder": 0,
      "settings": { "gutterX": 4, "gutterY": 4, "fullWidth": false, "alignItems": "stretch" },
      "children": [
        {
          "id": 11, "parentId": 10, "type": "Column", "sortOrder": 0,
          "settings": { "cols": { "xs": 12, "md": 8 } },
          "contents": [
            { "id": 100, "contentType": "ImageWithOverlay", "title": "DUVAR KAPLAMALARI",
              "imagePath": "/uploads/banners/2026/09/ds10.webp", "altText": "Duvar kaplama örneği",
              "linkUrl": "/duvar-kaplamalari", "buttonText": "İncele", "sortOrder": 0 }
          ],
          "children": []
        },
        {
          "id": 12, "parentId": 10, "type": "Column", "sortOrder": 1,
          "settings": { "cols": { "xs": 12, "md": 4 } },
          "contents": [],
          "children": [
            { "id": 13, "type": "Row", "sortOrder": 0, "settings": { "gutterY": 3 },
              "children": [
                { "id": 14, "type": "Column", "settings": { "cols": { "xs": 6, "md": 12 } }, "contents": [ /* ... */ ] },
                { "id": 15, "type": "Column", "settings": { "cols": { "xs": 6, "md": 12 } }, "contents": [ /* ... */ ] }
              ]
            }
          ]
        }
      ]
    }
  ],
  "deletedNodeIds": [],
  "deletedContentIds": []
}
```

* * *

## 9\. Teslim Edilecekler

Aşağıdaki dosyaların **tamamını, eksiksiz kod ile** üret:

1. Entity sınıfları \+ enum'lar \+ `IEntityTypeConfiguration` yapılandırmaları
2. `DbContext` değişiklikleri ve EF Core migration komutu
3. DTO'lar ve mapping
4. `IBannerService` / `BannerService` (tam implementasyon, cache dahil)
5. `BannerZoneViewComponent` \+ `Default.cshtml` \+ özyinelemeli `_Node.cshtml` \+ içerik tipi partial'ları
6. `BannerCssBuilder` yardımcı sınıfı (grid sınıfı ve CSS değişkeni üretimi) \+ **birim testleri**
7. `wwwroot/css/banner-builder.css` (ön yüz) ve `banner-admin.css` (panel)
8. `Areas/Admin/Controllers/BannerBuilderController.cs` \+ view'lar
9. `wwwroot/js/admin/banner-builder.js` (tam, çalışır)
10. Görsel yükleme servisi (ImageSharp ile)
11. `Program.cs` DI kayıtları ve Output Cache yapılandırması
12. Seed verisi: dekorras.com anasayfasına benzer **5 örnek banner bloğu** (biri iç içe kolon/satır içeren karmaşık düzen olsun)
13. Kısa `README-banner.md`\: kurulum adımları, `zoneKey` ile sayfaya ekleme örneği, genişletme noktaları

## 10\. Kalite Kriterleri

- Kod **derlenebilir** olmalı; `using` ifadeleri, namespace'ler eksiksiz.
- Nullable reference types açık; uyarısız derlensin.
- Async/await her yerde, `CancellationToken` taşınsın.
- Ön yüzde zone başına **en fazla 1 veritabanı sorgusu** (cache soğukken).
- Lighthouse: CLS \< 0.1, LCP görseli önceliklendirilmiş.
- 320px genişlikten 2560px'e kadar yatay kaydırma **oluşmamalı**.
- Tüm kullanıcı girdileri sunucuda doğrulansın ve sanitize edilsin.
- Türkçe karakter sorunları olmasın (kolasyon/encoding).
- Kod yorumları ve panel arayüzü metinleri **Türkçe**, sınıf/değişken adları İngilizce.

## 11\. Çalışma Şekli

Cevabını şu sırayla, aşamalı ver ve her aşamanın sonunda devam etmemi bekle:

1. **Aşama 1:** Veri modeli, DbContext yapılandırması, migration
2. **Aşama 2:** Servis katmanı \+ DTO'lar \+ cache
3. **Aşama 3:** Ön yüz render (ViewComponent, partial'lar, `BannerCssBuilder`)
4. **Aşama 4:** CSS dosyaları
5. **Aşama 5:** Admin controller \+ API
6. **Aşama 6:** Sürükle\-bırak editör JavaScript'i \+ admin view'ları
7. **Aşama 7:** Görsel yükleme, seed verisi, README

Herhangi bir yerde birden fazla makul yaklaşım varsa, **önce kısaca gerekçesini yaz, sonra birini seç ve uygula** — bana soru sorup bekleme.
