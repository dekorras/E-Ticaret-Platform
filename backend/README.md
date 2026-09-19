# Dekorras — ASP.NET Core 10 Onion Mimari

Bu, "Dekorras ASP.NET Core 9 Onion Mimari Dönüşüm Planı.md" belgesinde tarif edilen sistemin
Faz 0/1 (§13) çıktısıdır: Onion katmanları, Provider Registry çekirdeği, veritabanı şeması ve
temel CQRS akışları çalışır durumdadır.

> **Not — .NET sürümü:** Plan belgesi .NET 9 öneriyordu; ancak bu ortamda yalnızca .NET 10 SDK
> kurulu ve .NET 9 STS desteği zaten sona ermiş durumda (Kasım 2024 çıkışlı, ~18 ay destek).
> Bu nedenle proje **.NET 10 (LTS)** hedefiyle kuruldu — mimari kararların hiçbiri bundan etkilenmez.

## Mimari

```
src/Core/Dekorras.Domain            → Entity'ler, value object'ler, domain event'ler. SIFIR dış bağımlılık.
src/Core/Dekorras.Application       → CQRS (MediatR), FluentValidation, Provider Registry sözleşmeleri.
src/Infrastructure/Dekorras.Persistence   → EF Core (SQL Server), Identity, Repository/UnitOfWork.
src/Infrastructure/Dekorras.Infrastructure → ProviderRegistry çekirdeği + tüm connector'lar, Redis, Hangfire.
src/Presentation/Dekorras.Api        → REST API (JWT, Swagger, RFC 7807).
src/Presentation/Dekorras.Storefront → SSR mağaza ön yüzü + "/admin" altında Blazor Server admin
                                        paneli (bkz. "Admin Panel Yeniden Yapılandırma" - eskiden
                                        ayrı bir Dekorras.Admin uygulamasıydı, artık AYNI süreç/port).
tests/                                → xUnit + NSubstitute + FluentAssertions.
```

Bağımlılıklar her zaman içe akar: Domain hiçbir şeye bağımlı değildir; Application yalnızca
Domain'e bağımlıdır; Infrastructure/Persistence Application'a bağımlıdır; Presentation katmanları
üçüne de bağımlıdır.

## Provider Registry (§4.1) — nasıl çalışır

Ödeme, kargo, pazaryeri ve e-Fatura sağlayıcılarının tümü aynı kalıbı izler:

1. `Dekorras.Application/Common/Interfaces` içinde 4 sözleşme var: `IPaymentGateway`,
   `ICargoProvider`, `IMarketplaceConnector`, `IEInvoiceProvider` — hepsi `IIntegrationConnector`'dan türer.
2. `Dekorras.Infrastructure` içinde her sağlayıcı için bir sınıf yazılıp DI container'a kaydedilir
   (bkz. `Infrastructure/DependencyInjection.cs`). Şu an kodda yazılı olan 22 connector:
   - Ödeme: iyzico, PayTR, Param, PayPal, Stripe, Banka Havale/EFT
   - Kargo: Yurtiçi, Aras, MNG, DHL, UPS, FedEx
   - Pazaryeri: Trendyol, Hepsiburada, N11, İdefix, Amazon
   - e-Fatura: BizimHesap, Nilvera, Uyumsoft, Foriba, İzibiz
3. Kayıtlı olmak AKTİF olmak anlamına gelmez. Aktivasyon, admin panelin `/integrations`
   ekranından bir connector'ın `ConfigFieldDefinition` listesine göre otomatik render edilen
   formunu doldurup kaydetmekle olur (`ConfigureIntegrationProviderCommand`). Kayıt başarılı
   bağlantı testinden (`TestConnectionAsync`) geçerse `IntegrationProvider.Status = Active` olur.
4. **Yeni bir connector eklemek:** ilgili interface'i implemente eden yeni bir sınıf yazıp
   `Infrastructure/DependencyInjection.cs`'e bir `services.AddSingleton<...>()` satırı eklemek
   yeterlidir — admin UI'da elle yeni bir ekran yazmaya gerek YOKTUR, form `ConfigFieldDefinition`'dan üretilir.

Şu anki connector implementasyonları, gerçek sağlayıcı API'lerine bağlanan **iskeletlerdir**
(config alanları ve health-check akışı gerçek, dış HTTP çağrıları henüz simüle edilmektedir).
Gerçek entegrasyon, ilgili sağlayıcının API kimlik bilgileri elde edildiğinde tamamlanacaktır.

## Admin panelinde giriş

**Admin paneli artık `Dekorras.Storefront` içinde `/admin` altında çalışıyor** (bkz. "Admin Panel
Yeniden Yapılandırma" — eskiden ayrı bir `Dekorras.Admin` uygulaması/portuydu, Faz 4'te Storefront'a
birleştirildi). Blazor Server + ASP.NET Core Identity çerez tabanlı kimlik doğrulaması çalışır
durumdadır (uçtan uca test edilmiştir — bkz. altında). Geliştirme ortamında otomatik oluşturulan
başlangıç yönetici hesabı:

- E-posta: `admin@dekorras.com`
- Şifre: `Dekorras38*`
- URL: `http://localhost:5297/admin/login` (Storefront'la AYNI port)

Bu hesap hem "Yönetici" (her iki modüle de erişim) hem de "SysAdmin" (yetkiden bağımsız TAM erişim,
bkz. RBAC bölümü) rolleriyle seed edilir. `/admin/login` sayfası bilinçli olarak düz bir HTML
`<form>` ile `/admin/Account/Login` (POST, minimal API) uç noktasına gönderim yapar — Blazor'un
etkileşimli (interactive server) render modu üzerinden DEĞİL, çünkü oturum çerezinin yazılabilmesi
için sıradan bir HTTP istek/yanıt döngüsü gerekir. Storefront ve Admin AYNI paylaşılan
`"Identity.Application"` çerez şemasını kullandığı için (AYNI `AspNetUsers` tablosu) tek bir global
`LoginPath` OLAMAZ — `ConfigureApplicationCookie`'nin yönlendirme event'leri istek yoluna göre
DALLANIR: `/admin/**` için `/admin/login`e, Storefront'un KENDİ korumalı sayfaları için kendi
`/Account/Login`ine (bkz. "Admin Panel Yeniden Yapılandırma" Faz 4 - iki ayrı giriş hedefinin
birbirine KARIŞMADIĞI canlı doğrulandı).

**RBAC çalışır durumda ve uçtan uca test edilmiştir:** oturum açıldığında `AppUserClaimsPrincipalFactory`
(bkz. `Dekorras.Persistence/Security`), kullanıcının `AdminProfile → Role → Permission` zincirinden
gelen tüm izinleri `"permission"` claim'i, "SysAdmin" rolü ayrıca bir `"sysadmin"` claim'i olarak
oturum çerezine gömer. Sol menü (`NavMenu.razor`) TAMAMEN `<AuthorizeView>` ile sarılıdır - oturum
kapalıyken hiçbir menü öğesi görünmez; E-Ticaret/Muhasebe grupları `AccountingAccess`/
`ECommerceAccess` politikalarıyla (SysAdmin bunlardan BAĞIMSIZ olarak her zaman geçer), "Sistem
Yönetimi" grubu YALNIZCA `SysAdminOnly` politikasıyla filtrelenir - bu politikalar hem menü
GÖRÜNÜRLÜĞÜNDE hem de ilgili sayfaların `@attribute [Authorize(Policy = "...")]` seviyesinde
uygulanır (yalnızca menüyü gizlemek yetmez, doğrudan URL ile erişim de engellenmelidir - canlı test
edildi: bir izin veritabanından kaldırılıp yeniden giriş yapıldığında ilgili menü kayboluyor VE
sayfaya doğrudan gidiş `/admin/Account/AccessDenied`'a yönlendiriliyor). Son seçilen modül
`AdminProfile.LastSelectedModule`'da hatırlanır (`SetLastSelectedModuleCommand`).

## Kurulum

Gerekli: .NET 10 SDK, SQL Server (Express yeterli), (opsiyonel) Redis. **Docker KULLANILMIYOR** -
proje bilinçli olarak yerel bir SQL Server örneğine (bu ortamda `localhost\SQLEXPRESS`) karşı
çalışacak şekilde kurulur/geliştirilir; konteynerleştirme bilinçli olarak kapsam dışıdır.

```powershell
# appsettings.json içindeki ConnectionStrings:DefaultConnection kendi SQL Server'ınıza göre ayarlanmalı.
cd backend
dotnet tool restore # veya: dotnet tool install --global dotnet-ef

dotnet ef database update --project src/Infrastructure/Dekorras.Persistence --startup-project src/Infrastructure/Dekorras.Persistence

dotnet run --project src/Presentation/Dekorras.Api        # https://localhost:xxxx/swagger
dotnet run --project src/Presentation/Dekorras.Admin      # Modül Seçimi + Entegrasyonlar ekranı
dotnet run --project src/Presentation/Dekorras.Storefront # iskelet
```

### IIS Express ile çalıştırma (alternatif)

Visual Studio kurulu değilse (bu ortamda kurulu değil), IIS Express'i elle yapılandırmak gerekir -
bkz. proje hafızasındaki "devamı 64" notu için tam gerekçe/detay. Özet:

1. **ASP.NET Core Hosting Bundle** kurulu olmalı (`winget install --id Microsoft.DotNet.HostingBundle.10`) -
   IIS Express varsayılan olarak yalnızca eski (2018) ASP.NET Core Module V1'i içerir, .NET 10 için
   V2 gerekir.
2. Kullanıcının `%USERPROFILE%\Documents\IISExpress\config\applicationhost.config` dosyasına üç site
   (`Dekorras.Admin`:5035, `Dekorras.Storefront`:5297, `Dekorras.Api`:5199) + karşılık gelen üç
   `<location>` bloğu (`hostingModel="OutOfProcess"`, `processPath="dotnet"`,
   `arguments=".\<Proje>.dll"`) eklendi - fiziksel yol her projenin `bin\Debug\net10.0` çıktısını
   gösterir, bu yüzden IIS Express'i başlatmadan ÖNCE `dotnet build` çalıştırılmış olmalı.
3. Çalıştırmak için (her biri ayrı bir `iisexpress.exe` süreci):
   ```powershell
   & "C:\Program Files\IIS Express\iisexpress.exe" /config:"$env:USERPROFILE\Documents\IISExpress\config\applicationhost.config" /site:Dekorras.Admin
   & "C:\Program Files\IIS Express\iisexpress.exe" /config:"$env:USERPROFILE\Documents\IISExpress\config\applicationhost.config" /site:Dekorras.Storefront
   & "C:\Program Files\IIS Express\iisexpress.exe" /config:"$env:USERPROFILE\Documents\IISExpress\config\applicationhost.config" /site:Dekorras.Api
   ```
4. Orijinal (hiç özelleştirilmemiş) `applicationhost.config` `applicationhost.config.bak` olarak
   aynı klasörde yedeklendi.

## Katalog Yönetimi (Faz 2)

Admin panelinde çalışan katalog CRUD ekranları: `/ecommerce/categories` (sınırsız derinlikte
kategori ağacı; ↑/↓ ile sıralama — görsel sürükle-bırak yerine, `MoveCategoryCommand` aynı sonucu
verir), `/ecommerce/brands` (marka listesi/oluşturma), `/ecommerce/products` ve
`/ecommerce/products/{id|new}` (ürün listesi + temel alanlarla oluşturma/düzenleme: ad, slug, ürün
kodu, fiyat, KDV, birim (adet/m²/mt), min. satış adedi, stok, marka, kategori(ler)). Ürünün 11
sekmesinin geri kalanı (görsel, video, varyant, özellik, adet indirimi, kampanya, puan, custom
field, tasarım — bkz. plan §2.4) bu pasta kapsam dışıdır.

**Faz 2 sırasında bulunup düzeltilen gerçek bir hata:** `GetCategoryTreeQuery`, `GetCategoryByIdQuery`
ve `GetProductByIdQuery` ilk yazıldıklarında entity'leri `Include` olmadan materyalize edip
ardından `Translations`/`ProductCategories` gezinme özelliklerine dokunuyordu — EF Core bunları
sessizce boş koleksiyon olarak döndürüyordu (isimler yerine slug'a düşüyordu). Gerçek bir kategori
SQL ile eklenip admin ekranında görüntülenerek tespit edildi; düzeltme, DTO'ları entity hâlâ
`IQueryable` iken `.Select()` projeksiyonu içinde kurmak oldu (EF Core bunu doğru SQL'e çeviriyor,
`Dekorras.Application`'a EF Core referansı eklemeden).

## Önemli mimari not: yeni entity'ler nasıl doğru izlenir (EF Core + istemci taraflı Guid anahtarlar)

Bu projede TÜM entity Id'leri (bkz. `BaseEntity.Id`) İSTEMCİ TARAFINDA (`Guid.NewGuid()`) üretilir,
veritabanı tarafından değil. Bu, EF Core'un "bir entity'nin anahtarı zaten atanmışsa muhtemelen
veritabanında zaten vardır" varsayılan sezgisiyle çakışır: **already-tracked bir aggregate'in
koleksiyonuna eklenen YENİ bir çocuk entity (ör. bir kategoriye ilk kez eklenen bir dil çevirisi,
bir siparişe eklenen yeni bir durum geçmişi kaydı, var olan bir sepete eklenen yeni bir ürün),
EF Core tarafından "Modified" sanılıp var olmayan bir satırı UPDATE etmeye çalışır ve
`DbUpdateConcurrencyException` fırlatılır** — bu, `repository.Update(entity)` çağrılsın ya da
çağrılmasın, EF Core'un TAMAMEN OTOMATİK değişiklik algılamasında bile olur.

Bunu çözmek için `BaseEntity.IsTransient` (varsayılan `true`) eklendi ve `Dekorras.Persistence`
içinde iki parça devreye sokuldu:
1. **`TransientTrackingInterceptor`** — EF Core bir entity'yi veritabanından GERÇEKTEN materyalize
   ettiğinde (yani entity zaten var olduğunda) `IsTransient`'ı `false`'a çeker.
2. **`ApplicationDbContext.SaveChangesAsync`** — `SaveChanges`'ten önce `ChangeTracker.DetectChanges()`
   çağırıp hâlâ `IsTransient == true` olan (yani gerçekten hiç kaydedilmemiş) her entity'nin
   durumunu açıkça `Added`'a çevirir.

**Bu iki parça birlikte var olduğu sürece**, Application katmanındaki komut işleyicileri
`repository.Update(entity)` çağırmaMALIdır (zaten hiçbirinde çağrılmıyor) - EF Core'un otomatik
değişiklik algılaması hem skaler alan değişikliklerini hem de yeni eklenen alt entity'leri doğru
şekilde saptar. Yeni bir CQRS komutu yazarken, ALREADY-FETCHED bir aggregate'in koleksiyonuna yeni
bir çocuk ekleyen bir domain metodu çağırıyorsanız (`SetTranslation`, `AddOrUpdateItem`,
`TransitionTo`, `SetConfigValue`, `SetCategories` gibi), **hiçbir ek işlem yapmanıza gerek yok** -
bu mekanizma otomatik çalışır. Yalnızca koleksiyonu ÖNCE `IRepository<T>.LoadCollectionAsync` ile
yüklediğinizden emin olun (aksi halde domain metodu "var olan" bir çocuğu bulamayıp gereksiz yere
yeni bir tane daha ekler - ayrı bir hata sınıfı, bkz. `IRepository<T>.LoadCollectionAsync` XML dokümanı).

Bu hata, tam bir checkout akışı (katalog → sepet → ödeme sağlayıcı aktivasyonu → sipariş durum
makinesi → otomatik fatura taslağı) uçtan uca test edilirken yakalandı - bkz.
`tests/Dekorras.IntegrationTests/Ordering/FullCheckoutFlowTests.cs` ve
`tests/Dekorras.IntegrationTests/Catalog/CategoryUpdateRegressionTests.cs`. Bu testler gerçek SQL
Server'a karşı çalışır; NSubstitute mock'ları bu sınıf hatayı YAKALAYAMAZ.

Ayrıca: `Dekorras.Admin`, `Dekorras.Api` ve `Dekorras.Storefront` AYRI ASP.NET Core uygulamaları
olduğundan, Data Protection (sağlayıcı anahtarlarının şifrelenmesi, §4.1) için `SetApplicationName("Dekorras")`
+ `PersistKeysToFileSystem` ile ortak bir anahtar deposu paylaşmaları sağlandı - aksi halde Admin'de
şifrelenen bir sağlayıcı anahtarı Api/Storefront tarafından çözülemezdi.

## Storefront (Faz 3 - ilk dilim)

`Dekorras.Storefront` artık gerçek verilerle çalışan bir SSR mağazadır: anasayfa (aktif ürün
ızgarası), `/Category/Index?slug=...` (kategori sayfası + ürünleri), `/Product/Details?slug=...`
(ürün detayı). Üst menü (`MainMenuViewComponent`) yalnızca **Aktif** kategorileri gösterir.
Bunlar için Application katmanına ayrı, müşteri-yüzü sorguları eklendi
(`Dekorras.Application/Catalog/Storefront/*`) - admin'in kendi sorgularından BİLİNÇLİ olarak
ayrıldı çünkü müşteri asla bir taslak ürünü veya pasif bir kategoriyi görmemeli; admin ekranları
ise tam tersine hepsini görebilmeli. **Canlı olarak test edildi:** gerçek bir kategori/ürün SQL ile
eklenip anasayfa/kategori/ürün sayfalarında göründüğü doğrulandı; ardından kategori pasife alınıp
ürün taslağa çekilerek her ikisinin de doğru şekilde 404 döndüğü (mevcut URL slug yapısının
korunması + yetkisiz/yayınlanmamış içeriğin gizlenmesi ilkesi) doğrulandı.

Sepet/checkout, çok dil/RTL, arama/filtreleme ve CMS/blog sayfaları bu dilimin kapsamı dışındadır.

## Sepet ve Checkout (Faz 4 - ilk dilim)

Storefront'ta uçtan uca çalışan bir misafir alışverişi akışı var: `/Cart` (ekle/adet güncelle/kaldır,
HttpOnly çerezle tanımlanan misafir sepeti - üyelik gerektirmez), `/Checkout` (ad/e-posta/telefon/
adres formu + Provider Registry'den gelen AKTİF ödeme VE kargo sağlayıcıları arasından seçim),
`/Checkout/Confirmation`. `PlaceOrderCommand` şunları tek bir iş biriminde yapar: misafir için hafif
bir Customer/Address kaydı oluşturur, sepeti bir `Order`'a çevirir, seçilen `ICargoProvider`'ın
`GetRateAsync`'ini çağırıp kargo ücretini siparişe ekler, seçilen `IPaymentGateway`'in
`AuthorizeAsync`'ini ÇAĞIRIR (Provider Registry'den şifresi çözülmüş gerçek yapılandırmayla) ve
sonucu bir `Payment`/`Transaction` olarak kaydeder, sonra sepeti temizler. Sipariş varsayılan olarak
"Onay Bekliyor" durumunda başlar; `TransitionOrderStatusCommand` ile Tamamlandı'ya taşındığında
Muhasebe modülünde otomatik bir fatura taslağı oluştuğu uçtan uca doğrulanmıştır (yukarıdaki bölüme
bakınız).

**Tüm akış gerçek HTTP istekleriyle de canlı test edilmiştir** (ürün sayfası → sepete ekle →
checkout → sipariş tamamlama → onay sayfası), gerçekten çalışan `bank-transfer` + `yurtici-kargo`
sağlayıcılarıyla; matematik doğrulanmıştır (250 TRY x 2 adet = 500 taban + %20 KDV=100 +
2kg x 15 TRY kargo=30 → 630 TRY toplam). Bu sırada iki gerçek hata daha bulunup düzeltildi:
(1) ürün sayfasındaki "Sepete Ekle" formunun gizli `returnUrl` alanı `Request.Path` kullanıyordu
(sorgu dizesini/`?slug=...`'ı kaybediyordu) - `Url.Action(...)` ile düzeltildi; (2) `Order` entity'sine
eklenen `ShippingProviderId` alanı için migration oluşturulmamıştı (`AddOrderShippingProvider`
migration'ı ile düzeltildi - **yeni bir entity alanı eklerken migration oluşturmayı unutmayın**).

Gerçek ödeme sağlayıcı API çağrıları (hâlâ stub), sipariş takibi sayfası ve kayıtlı müşteri girişi
bu dilimin kapsamı dışındadır.

## Kayıtlı Müşteri Hesapları (Faz 4 - ikinci dilim)

Storefront'un kendi müşteri hesapları var — Admin panelinin yönetici girişinden TAMAMEN AYRI
(aynı `AspNetUsers` tablosunu paylaşırlar ama bir müşterinin `AdminProfile`/`Role`/`Permission`
kaydı hiç olmaz, dolayısıyla Admin'in RBAC policy'lerinden hiçbirini karşılamaz). `/Account/Register`,
`/Account/Login`, `/Account/Logout`, `/Account/Orders` (Hesabım > Siparişlerim). Kayıtlı bir müşteri
checkout'a girdiğinde form otomatik olarak ad/e-posta ile dolduruluyor ve `PlaceOrderCommand` HER
seferinde yeni bir misafir Customer YARATMAK yerine mevcut Customer kaydını kullanıyor (yeni bir
Address her zaman eklenir - farklı teslimat adresleri desteklenir). Storefront geleneksel MVC
olduğundan (Blazor Server değil), Admin'deki "düz form + minimal API" atlatmasına gerek kalmadı -
`UserManager`/`SignInManager` doğrudan `AccountController` içinde kullanılıyor.

**Canlı HTTP testinde (kayıt ol → giriş yap → checkout → sipariş geçmişi → çıkış yap akışı gerçek
istemciyle sürülerek) bir gerçek hata daha bulunup düzeltildi:** `TransientTrackingInterceptor`
her `OnConfiguring` çağrısında (yani normalde İSTEK BAŞINA BİR KEZ) `new TransientTrackingInterceptor()`
ile YENİDEN oluşturuluyordu. EF Core bunu "farklı bir yapılandırma" sanıp her istek için ayrı bir
dahili service provider inşa ediyordu; ~20 istekten sonra "ManyServiceProvidersCreatedWarning" bir
`InvalidOperationException` olarak fırlatılıp tüm siteyi çökertiyordu. Düzeltme: interceptor artık
paylaşılan, durumsuz, `static readonly` TEK bir örnek. **Bu, yalnızca gerçek/sürdürülen canlı
trafik altında ortaya çıkan bir hata sınıfıydı - birim/entegrasyon testleri (her biri yalnızca
birkaç DbContext örneği oluşturuyor) bunu asla yakalayamazdı; yalnızca bu tür ardışık, çok istekli
canlı smoke testleri yakalayabilirdi.**

## Sipariş Yönetimi (Admin)

`/ecommerce/orders` (durum filtresine göre listeleme - kaynağı fark etmeksizin tüm siparişler tek
ekranda birleşir, bkz. plan §9.1) ve `/ecommerce/orders/{id}` (ürünler, müşteri/teslimat bilgisi,
ödeme durumu, durum geçmişi, ve **yalnızca durum makinesinin izin verdiği** sonraki durumlara geçiş
düğmeleri - `Order.GetValidNextStatuses()` üzerinden). "Kargoya Verildi"ye geçiş özel olarak bir
kargo takip numarası girişi ister (`AttachTrackingNumberCommand`). Canlı olarak doğrulandı: gerçek
bir sipariş `PlaceOrderCommand` ile oluşturulup admin ekranlarında doğru göründüğü, ve
`PendingApproval` durumundaki bir siparişin tam olarak `OrderStatusTransitionRules`'ın izin verdiği
6 düğmeyi (Hazırlanıyor/Reddedildi/İptal Edildi/Süresi Doldu/Başarısız/Durduruldu) gösterdiği teyit
edildi.

## Ürün Görselleri

Admin'de ürün düzenleme ekranına (`/ecommerce/products/{id}`) bir "Görseller" bölümü eklendi:
dosya yükleme (`InputFile`), her görsel için "Ana Görsel Yap" ve "Sil" düğmeleri. Storefront artık
ürün ızgarasında (ana görsel, yoksa "Görsel Yok" yer tutucusu) ve ürün detay sayfasında (ana görsel
+ küçük resim şeridi) gerçek görseller gösteriyor.

**Bu özellik için önden bulunup düzeltilen bir mimari gerçek:** `IFileStorage`'ın yerel diskteki
karşılığı (`LocalFileStorage`), her Presentation projesinin (Admin/Api/Storefront) KENDİ
`AppContext.BaseDirectory`'sine yazıyordu - Admin'de yüklenen bir görsel, AYRI bir süreç olan
Storefront'ta hiç görünmeyecekti (Data Protection anahtarlarıyla daha önce yaşanan sorunun aynısı).
Düzeltme: `LocalFileStorage.SharedUploadsRoot` artık üçü için de ortak, `%LOCALAPPDATA%\Dekorras\uploads`
altında paylaşılan bir klasör; her Presentation projesinin `Program.cs`'i bu klasörü `/uploads`
altında `UseStaticFiles` ile sunuyor. Çapraz-süreç dosya paylaşımı gerçek bir dosya yerleştirip
Storefront'tan HTTP ile çekerek doğrulandı - production'da bunun yerine gerçek bir Blob/S3
deposu kullanılmalıdır (kod içinde TODO olarak işaretli).

Görsel ekleme/silme/ana görsel değiştirme komutları da bu oturumdaki `LoadCollectionAsync` +
"yeni entity mi, mevcut mu" deseni izlenerek yazıldı ve gerçek SQL Server'a karşı regresyon
testleriyle doğrulandı (bkz. `tests/Dekorras.IntegrationTests/Catalog/ProductImageRegressionTests.cs`).

## Kuponlar

Admin'de `/ecommerce/coupons` (kod, yüzde/sabit tutar indirimi, geçerlilik tarihi aralığı, kullanım
limiti ile kupon oluşturma + aktif/pasif etme). Storefront'ta sepet sayfasında bir kupon kodu
girilip uygulanabiliyor (kod büyük harfe normalize edilir); sepet ve checkout sayfaları indirimi ve
yeni toplamı gösteriyor. `PlaceOrderCommand`, checkout ANINDA kuponu yeniden doğrular (sepete
eklendiğinden beri süresi dolmuş veya limite ulaşmış olabilir), geçerliyse `Order.DiscountTotalTry`'a
yansıtır ve kuponun kullanım sayacını artırır. KDV, indirim ÖNCESİ taban tutar üzerinden hesaplanır
(mevzuata uygun sıralama). Hem tam kupon→sipariş akışı (gerçek SQL Server'a karşı entegrasyon
testiyle: %10 indirim, KDV, kargo ve son toplamın doğru hesaplandığı, kupon kullanım sayacının
arttığı) hem de Admin/Storefront ekranları gerçek HTTP istekleriyle canlı doğrulandı.

## Müşteri Yönetimi (Admin)

Admin'de `/ecommerce/customers` (arama: ad/e-posta, üye/misafir rozeti, sipariş sayısı, aktif/pasif
durumu) ve `/ecommerce/customers/{id}` (adresler, sipariş geçmişi tablosu, aktif/pasif etme
butonu). Misafir siparişlerinden otomatik oluşturulan `Customer` kayıtları (`IdentityUserId` "guest-"
ile başlar) listede "Misafir" rozetiyle, gerçek üyeler "Üye" rozetiyle ayırt edilir — aynı `Customer`
aggregate'i her iki durumda da kullanılıyor (bkz. plan §2.2 "misafir alışverişi"). Sipariş sayısı,
tüm siparişler üzerinde `CustomerId`'ye göre gruplanarak tek seferde hesaplanır (N+1 sorgu yok).

`SetCustomerActiveCommand` diğer komutlar gibi `Update(customer)` çağırmaz (bkz. yukarıdaki EF Core
notu) — bir regresyon testiyle özellikle doğrulandı: müşteri önce pasife alınıp sonra tekrar
aktifleştiriliyor, ikinci `SaveChanges` çağrısının zaten yüklü adres koleksiyonunu yanlışlıkla
"Added" durumuna düşürüp yinelenen satır oluşturmadığı kontrol ediliyor
(`CustomerManagementRegressionTests.AktifPasifDegistirme_...`). Ayrıca `GetCustomersQuery`'nin
misafir/üye ayrımını ve arama filtresini doğru uyguladığını kontrol eden ikinci bir test var. Gerçek
veriyle (1 kayıtlı + 1 misafir müşteri, her birinin 1 siparişi) Admin uygulaması çalışır durumdayken
canlı HTTP istekleriyle liste ve detay sayfaları doğrulandı; test verisi sonrasında veritabanından
temizlendi.

## Sipariş Takip Sayfası (Storefront)

`/Account/Orders` sipariş listesinden artık `/Account/OrderDetail/{id}`'ye gidiliyor: ürünler, ara
toplam/KDV/kargo/genel toplam, teslimat adresi, kargo takip no (varsa) ve durum geçmişi zaman
çizelgesi (`GetOrderByIdQuery` — Admin'in de kullandığı aynı sorgu). **Sahiplik kontrolü** kritik:
`AccountController.OrderDetail` önce `GetMyOrdersQuery` ile isteği yapan müşterinin KENDİ sipariş
ID'leri arasında olduğunu doğruluyor, aksi halde 404 dönüyor — aksi halde bir müşteri başka birinin
sipariş GUID'ini tahmin ederek adres/tutar/ürün bilgilerini görebilirdi (`GetOrderByIdQuery` tek
başına herhangi bir ID için detay döner, çünkü Admin panelinde TÜM siparişleri görüntülemek için
bilinçli olarak böyle tasarlandı). Canlı HTTP testiyle üç senaryo doğrulandı: sahibi kendi siparişini
görebiliyor, BAŞKA bir müşteri aynı siparişi 404 alıyor, anonim istek `/login`'e yönlendiriliyor.

**Bu turda gerçek bir veri hatası bulundu ve düzeltildi:** `PlaceOrderCommand`, sipariş kalemine
(`OrderItem.ProductName`) müşterinin göreceği ürün ADI yerine yanlışlıkla `product.ProductCode`
(SKU) yazıyordu — checkout'tan Faz 4'ün ilk diliminde (bu oturumun 5. turu) beri var olan bir hataydı,
şimdiye kadar hiçbir ekran/test bunu fark etmemişti çünkü Admin sipariş listesi/detayı ve mevcut
testler kalem ADINI hiç göstermiyor/doğrulamıyordu; bu YENİ sayfa canlı testte "SMOKEORD-001" gibi
bir SKU gösterince ortaya çıktı. Düzeltme: `ProductTranslation` ayrı bir sorguyla (`product`
zaten pasif gezinme ile materyalize edildiği için `product.Translations`'a doğrudan dokunulamaz —
bkz. yukarıdaki EF Core notu) çekilip gerçek ürün adı yazılıyor, çeviri yoksa `ProductCode`'a düşüyor.
`FullCheckoutFlowTests`'e bunu koruyan bir assertion eklendi (`orderItem.ProductName == "Test Ürün"`,
"FF-001" değil). **Ders:** DTO'da gösterilmeyen/test edilmeyen bir alan sessizce yanlış veri
taşıyabilir — yeni bir ekran eskiden yazılmış bir alanı ilk kez GÖRÜNÜR kılınca ortaya çıkabilir.

## Api - JWT Kimlik Doğrulama (mobil/üçüncü taraf istemciler)

`Dekorras.Api` altında `/api/v1/auth/register`, `/login`, `/refresh`, `/logout` uç noktaları -
Admin/Storefront'un çerez tabanlı girişinden TAMAMEN AYRI (aynı `AspNetUsers` tablosu paylaşılır).
Erişim jetonu (JWT, `Jwt:AccessTokenMinutes` süreli) her istekte `Authorization: Bearer` başlığıyla
gönderilir; yenileme jetonu (opak, rastgele 64 bayt, `Jwt:RefreshTokenDays` süreli) **rotasyonlu**
çalışır - `Dekorras.Domain.Identity.RefreshToken` entity'si veritabanında yalnızca ham jetonun
SHA-256 özetini saklar (ham jeton yalnızca istemciye döner). Her `/refresh` çağrısı eskisini iptal
edip yenisini verir; **eski (rotasyona uğramış) bir jeton tekrar kullanılmaya çalışılırsa 401 döner**
(çalıntı jeton tekrar oynatma koruması). `RotateRefreshTokenCommand`/`RevokeRefreshTokenCommand`/
`IssueRefreshTokenCommand` bu akışı yönetir - Application katmanı ASP.NET Core Identity'ye hiç
bağımlı değildir (yalnızca `identityUserId` string'i taşır, `UserManager`/`IdentityUser` sadece
`AuthController`'da - Storefront'un `AccountController`'ıyla aynı ayrım deseni).

Gerçek SQL Server'a karşı 3 regresyon testiyle (rotasyonun eskiyi geçersiz kıldığı, rotasyona
uğramış bir jetonun VE hiç var olmayan bir jetonun reddedildiği, çıkışın jetonu iptal ettiği) VE
çalışan Api'ye karşı gerçek HTTP istekleriyle (kayıt→giriş→korumalı endpoint→yenileme→ESKİ jetonla
tekrar deneme→çıkış→çıkış SONRASI yenileme denemesi→yinelenen e-posta ile kayıt) uçtan uca
doğrulandı - hepsi beklenen durum kodlarını verdi (401/400/204/200).

## Ürün Varyantları (Admin)

`/ecommerce/products/{id}` sayfasına "Varyantlar" bölümü eklendi (Görseller'in altında) - farklı
ölçü/renk gibi seçenekler için ayrı SKU, taban fiyata eklenen fark (`PriceAdjustmentTry`, negatif de
olabilir) ve kendi stok adediyle ekleme/düzenleme/silme. `Dekorras.Domain.Catalog.ProductVariant`
zaten Faz 0/1'de tanımlıydı ama hiç Application/Admin katmanı yoktu - bu turda tamamlandı:
`AddProductVariantCommand`/`UpdateProductVariantCommand`/`RemoveProductVariantCommand`/
`GetProductVariantsQuery`, hepsi `LoadCollectionAsync` + "yeni mi mevcut mu" deseniyle (bkz. yukarıdaki
EF Core notu). SKU alanına `Products.ProductCode` ile aynı gerekçeyle benzersiz indeks eklendi
(`AddProductVariantSkuIndex` migration'ı). 2 yeni regresyon testiyle (ikinci varyant ekleme/güncelleme
hata vermiyor, bir varyant silinince diğeri etkilenmiyor) VE çalışan Admin'e karşı canlı HTTP testiyle
(gerçek bir ürüne varyant eklenip sayfa yeniden yüklendiğinde SKU/seçenek/fiyat farkı/stokun doğru
göründüğü) doğrulandı.

## Storefront Varyant Seçimi (Sepet/Checkout)

Ürünün varyantı varsa `/Product/Details` sayfasında bir "Seçenek" açılır kutusu görünür (fiyat farkı
parantez içinde: `Ölçü: 100x100 (+75,00 ₺)`). Seçilen varyant `Cart/Add`'e `variantId` olarak
gönderilir; `CartItem`/`OrderItem` artık `VariantId` (nullable) taşıyor
(`AddCartAndOrderItemVariantId` migration'ı) - aynı ürünün FARKLI varyantları sepette AYRI satırlar
olarak tutulur (`Cart.AddOrUpdateItem`/`RemoveItem` artık `(ProductId, VariantId)` çiftiyle eşleşiyor).
Birim fiyat her zaman `product.BasePriceTry + variant.PriceAdjustmentTry` olarak hesaplanır - hem
sepete eklerken hem de **checkout ANINDA tekrar** (taban ürün fiyatı gibi, sepete eklendiğinden beri
değişmiş olabilir). Sipariş kalemine yazılan üründe adı varyant etiketiyle birleştirilir (`"Ürün Adı
(Ölçü: 100x100)"`).

Gerçek SQL Server'a karşı bir entegrasyon testiyle (sepet önizlemesinde fiyat farkının doğru
göründüğü, sipariş kaleminin doğru `VariantId`/fiyat/etiketle kaydedildiği) VE çalışan Storefront'a
karşı UÇTAN UCA gerçek HTTP akışıyla (ürün sayfası → varyant seç → sepete ekle → sepette doğru fiyat/
etiket → checkout özetinde doğru → sipariş tamamlanınca DB'de doğru `VariantId`/tutar) doğrulandı.

## Stok Takibi ve Düşümü (Checkout)

`PlaceOrderCommand` artık checkout ANINDA stok doğruluyor VE düşüyor - önceki turlarda bu hiç yoktu
(`Product.StockQuantity` yalnızca admin'de elle ayarlanıyordu, sipariş verildiğinde hiç değişmiyordu).
Varyantsız bir üründe (ve yalnızca `Product.TrackStock` true ise) taban stoktan düşülür; varyantlı
bir kalemde ise varyantın KENDİ stoku düşülür (taban ürün stoku etkilenmez - her varyant ayrı stok
taşır). İstenen miktar mevcut stoku aşarsa `InvalidOperationException` fırlatılır - bu, sipariş/ödeme
kaydı oluşmadan ÖNCE (metodun en başında) gerçekleşir, yani reddedilen bir sipariş hiçbir kalıcı iz
bırakmaz (ne stok değişir ne sahte bir Order satırı kalır). `Product.UpdateStock` zaten `TrackStock`
true ise `StockAvailability`'yi de günceller (stok 0'a düşünce ürün otomatik "Stokta Yok" olur -
Storefront ürün sayfası ve "Sepete Ekle" butonu buna göre değişir, ek bir kod gerekmedi).

Storefront'ta sepete stoktan FAZLA miktar eklemek hâlâ mümkün (sepet aşaması bilinçli olarak
doğrulama yapmaz - "iyimser sepet, yetkili checkout" deseni), ama checkout'ta reddedilir;
`CheckoutController.PlaceOrder` bu `InvalidOperationException`'ı yakalayıp ham bir 500 sayfası yerine
checkout formunu hata mesajıyla ("'X' için yeterli stok yok. Mevcut: 2, istenen: 5.") yeniden gösterir.

3 yeni entegrasyon testiyle (başarılı siparişte stok doğru düşüyor, yetersiz stokta sipariş
reddedilip stok/Order DEĞİŞMİYOR, varyant stoku taban ürünü etkilemeden düşüyor) VE çalışan
Storefront'a karşı canlı HTTP akışıyla (2 stoklu ürüne 5 adet eklenip checkout'ta reddedildiği,
sonra 2 adede düşürülüp başarıyla tamamlandığı, ardından ürün sayfasının "Stokta Yok" gösterdiği)
doğrulandı.

## Toplu Alım İndirimi (Quantity Discount)

`Dekorras.Domain.Catalog.QuantityDiscount` (min. adet + o adette geçerli sabit birim fiyat) Faz 0/1'de
tanımlıydı ama hiç kullanılmıyordu - bu turda hem Admin CRUD'u (`/ecommerce/products/{id}` sayfasında
"Toplu Alım İndirimi" bölümü) hem de checkout'a bağlanması tamamlandı. Bir üründe birden fazla kademe
varsa, istenen adedi aşmayan EN YÜKSEK `MinimumQuantity` eşiği kullanılır (`AddCartItemCommand`/
`UpdateCartItemQuantityCommand`/`PlaceOrderCommand` üçünde de aynı sorgu deseni - checkout'ta yine
FRESH yeniden hesaplanır, tıpkı varyant/taban fiyat gibi). Varyant fiyat farkı varsa kademe fiyatının
ÜZERİNE eklenir (taban fiyatın üzerine eklendiği gibi).

**Bu turda gerçek bir hata bulunup düzeltildi:** `Cart.AddOrUpdateItem`, bir sepet satırının adedi
DEĞİŞTİĞİNDE yalnızca `SetQuantity` çağırıyordu, birim fiyatı GÜNCELLEMİYORDU - bu, varyant fiyatı
için sorun değildi (adetten bağımsız) ama toplu alım kademesi için KRİTİK: 8 adetten 12 adete
çıkarken kademe eşiğine girilse bile fiyat eski (yüksek) değerde donuk kalırdı. Düzeltme:
`CartItem.SetUnitPrice` eklendi, `AddOrUpdateItem` artık adet güncellenirken çağıranın YENİDEN
hesapladığı fiyatı da uyguluyor. Gerçek SQL Server'a karşı bir entegrasyon testiyle (5 adette taban
fiyat, 12 adete çıkınca kademe fiyatı, 8'e geri düşünce taban fiyata dönüş, checkout'ta doğru
kaydedilme) VE çalışan Admin+Storefront'a karşı canlı HTTP akışıyla (Admin'de kademe eklenip
göründüğü, Storefront'ta 5→12 adet güncellemesinde sepet fiyatının 100→80 TRY'ye doğru düştüğü)
doğrulandı.

## Muhasebe Modülü — ilk dilim (Faz 8)

Admin'de `/accounting` altında `Cari Hesaplar` ve `Faturalar` ekranları eklendi - modül seçim
ekranındaki "Muhasebe Modülü" artık gerçek işlevsellik içeriyor (önceden yalnızca yer tutucu bir
sayfaydı).

- **Cari Hesaplar** (`/accounting/ledger-accounts`): liste (ad/vergi no arama, açık bakiye, hareket
  sayısı), detay (adres/vergi bilgisi + hareket geçmişi), elle cari hesap oluşturma (müşteri/
  tedarikçi/her ikisi), elle borç/alacak hareketi ekleme. `LedgerAccount.OpenBalanceTry` her
  hareket eklendiğinde otomatik güncellenir (`RecordTransaction` domain metodu zaten böyle
  tasarlanmıştı, yalnızca Application/Admin katmanı eksikti).
- **Faturalar** (`/accounting/invoices`): liste (durum filtresi, cari adı, ilişkili sipariş no),
  detay (kalemler + toplamlar), "İrsaliyeleştir" geçişi. Bir sipariş "Tamamlandı" olduğunda otomatik
  oluşan taslak faturalar (`OrderCompletedEventHandler` - bu otomasyon zaten çalışıyordu, bu turda
  yalnızca GÖRÜNTÜLENEBİLİR hale geldi) burada listeleniyor.

2 yeni regresyon testiyle (ikinci hareket eklemenin `DbUpdateConcurrencyException` fırlatmadığı ve
bakiyeyi doğru güncellediği; fatura detayının kalemleri/toplamları doğru döndürdüğü ve
irsaliyeleştirmenin durumu güncellediği) VE çalışan Admin'e karşı canlı HTTP akışıyla (gerçek bir
sipariş Tamamlandı'ya kadar ilerletilip otomatik taslak faturanın Admin'de doğru göründüğü, elle
oluşturulan bir tedarikçi carisine hareket eklenip bakiyenin doğru göründüğü) doğrulandı.

- **Kasa & Banka** (`/accounting/cash-and-bank`): kasa/banka hesabı oluşturma, yatırma/çekme.
  `CashRegister.Withdraw`/`BankAccount.Withdraw` yetersiz bakiyede `DomainException` fırlatır - Admin
  UI bunu yakalayıp kullanıcıya gösterir, bakiye ASLA negatife düşmez (regresyon testiyle
  doğrulandı: reddedilen bir çekim bakiyeyi hiç değiştirmiyor). **Not:** `LedgerAccount`'ın aksine
  bu iki entity'nin hareket geçmişi/denetim izi YOK (domain modeli yalnızca `BalanceTry`'yi tutuyor,
  `Deposit`/`Withdraw` geçmiş bırakmadan doğrudan mutasyon yapıyor) - bu bir Faz 0/1 tasarım kararı,
  bu turda genişletilmedi.
- **Giderler** (`/accounting/expenses`): kategori/tutar/tarih/açıklama ile gider kaydı, liste +
  toplam. Kasa/banka hesabından düşülen bir "ödeme" bağlantısı yok (domain modelinde `Expense`
  bağımsız bir kayıt - Kasa/Banka'ya bağlanması ayrı bir iş).

- **Çek & Senet** (`/accounting/checks-and-notes`): cari hesap seçilerek çek/senet kaydı, tahsil
  etme/karşılıksız işaretleme. `LedgerAccount.CheckNoteBalanceTry` Faz 0/1'den beri domain'de vardı
  ama hiçbir yazma yolu yoktu (her zaman 0'dı) - bu turda `AdjustCheckNoteBalance` eklenip
  kablolanıyor: kayıt eklendiğinde artıyor, tahsil/karşılıksız olarak sonuçlandırıldığında (artık
  "bekleyen" olmadığı için) düşüyor. Sonuçlandırılmış bir çek/senet TEKRAR tahsil/karşılıksız
  edilmeye çalışılırsa reddedilir (`InvalidOperationException`) - aksi halde bakiye ikinci kez
  yanlışlıkla düşürülürdü. 2 yeni regresyon testiyle (bakiyenin ekleme/tahsilde doğru güncellendiği,
  sonuçlandırılmış bir kaydın tekrar işlenmeye çalışılmasının reddedildiği) VE çalışan Admin'e karşı
  canlı HTTP akışıyla (bir çek+senet eklenip hem Çek&Senet sayfasında hem de ilgili Cari Hesap'ın
  detay sayfasında güncel bekleyen bakiyenin doğru göründüğü) doğrulandı.

- **Teklifler** (`/accounting/quotes`): cari hesap seçilerek teklif oluşturma, kalem ekleme
  (`Quote.TotalAmountTry` her kalemde otomatik yeniden hesaplanır), liste (geçerli/süresi
  dolmuş/siparişe dönüştü rozetleri). 1 yeni regresyon testiyle (ikinci kalem eklemenin hata
  vermediği, toplamın doğru hesaplandığı) VE çalışan Admin'e karşı canlı HTTP akışıyla (bir teklife
  iki kalem eklenip hem listede hem detayda doğru toplam/kalemlerin göründüğü) doğrulandı.
  `Quote.ConvertToOrder` daha sonra (devamı 63) gerçek bir Admin akışına bağlandı - bkz. "Teklifi
  Siparişe Dönüştürme" bölümü.

## Muhasebe Modülü — Tamamlama Turu (Faz 8 sonu)

Modülü "eksiksiz" hale getirmek için son bir tamamlama turu yapıldı - önceki turlarda bırakılan
kapsam dışı notlar ve fark edilen eksik yazma yolları kapatıldı:

- **Cari Hesap aktif/pasif etme:** `LedgerAccount.IsActive` Faz 0/1'den beri domain'de vardı ama
  hiçbir yazma yolu yoktu (her zaman `true`) - `Activate()`/`Deactivate()` eklendi, Cari Hesap
  detay sayfasına toggle butonu kondu (Müşteri Yönetimi'ndeki aynı desen).
- **Gerçek İrsaliye kaydı:** `MarkInvoiceAsWaybilledCommand` artık yalnızca `Invoice.Status`'u
  değiştirmiyor, kendi numarasıyla GERÇEK bir `Waybill` kaydı da oluşturuyor - `Waybill` entity'si
  Faz 0/1'den beri domain'de vardı ama hiç kullanılmıyordu. İrsaliye no artık Fatura detayında
  görünüyor.
- **e-Fatura/e-Arşiv elle kesim kaydı:** Gerçek GİB gönderimi bu sistemin kapsamında değil (bkz.
  plan §4.1 madde 8 - sertifikalı bir entegratöre BAĞLANILIR, sıfırdan KURULMAZ) ama admin resmi
  kesimi BizimHesap/Nilvera ekranından yaptıktan SONRA sonucu (resmi fatura no + kullanılan
  sağlayıcı) burada kaydedebiliyor - `IssueInvoiceAsEInvoiceCommand`/`IssueInvoiceAsEArchiveCommand`,
  `EInvoiceLog`'a (Faz 0/1'den beri kullanılmıyordu) denetim izi yazıyor. Yalnızca Entegrasyonlar'da
  **Aktif** olan bir e-Fatura sağlayıcısı seçilebilir - hiçbiri aktif değilse ekran bunu açıkça
  belirtiyor.
- **Raporlar** (`/accounting/reports`): Varlıklar/Borçlar özet panosu (BizimHesap'ın dashboard'undan
  esinlenildi, bkz. plan §14) - Kasa+Banka+Alacaklar toplamı, Borçlar+Bekleyen Çek/Senet toplamı,
  Net Durum, ayrıca kasa/banka/alacak/borç/gider/taslak fatura kartları ayrı ayrı.

3 yeni regresyon testiyle (aktif/pasif geçişi, İrsaliye+e-Fatura kesiminin gerçek kayıt/denetim izi
bıraktığı, dashboard toplamlarının doğru hesaplandığı) VE çalışan Admin'e karşı canlı HTTP akışıyla
(bir cari pasife alınıp rozetin değiştiği, taslak bir faturaya aktif bir e-Fatura sağlayıcısı
seçilebildiği, dashboard'ın gerçek DB verisini doğru yansıttığı) doğrulandı.

**Gerçekten kapsam dışı kalan (bu sistemin sınırı, "eksik" değil):** GİB'e fiili elektronik gönderim
(sertifikalı entegratör API'sinin GERÇEK HTTP çağrısı) - bu, üçüncü parti bir entegratör hesabı/API
kimlik bilgisi gerektirir ve plan bunu açıkça "sıfırdan kurma, bağlan" olarak tanımlıyor.
Teklif→Sipariş dönüştürme akışı da ayrı bir tasarım kararı gerektirdiği için kapsam dışı bırakıldı
(yukarıda açıklandı). **Bunların dışında Muhasebe modülünün TÜM ekranları ve orphaned alanları
tamamlandı.**

## Arama, Filtreleme ve SEO Meta Etiketleri (Faz 3 devamı)

Storefront'a arama kutusu (nav bar'da her sayfada görünür), `/Search?q=...` sonuç sayfası, ve
kategori/arama sayfalarında fiyat aralığı + marka filtresi eklendi (`GetStorefrontProductsQuery`
artık `SearchText`/`MinPriceTry`/`MaxPriceTry`/`BrandId` parametreleri alıyor - arama ürün adı VE
açıklamasında geçiyor, seçilen dile göre).

**Bulunan gerçek eksiklik:** `ProductTranslation.MetaTitle/MetaDescription/MetaKeywords` ve
`CategoryTranslation.MetaTitle/MetaDescription` alanları Faz 0/1'den beri domain'de vardı ama HİÇBİR
Admin komutu bunları set etmiyordu (her zaman `null` yazılıyordu) VE Storefront hiçbir view'da bu
alanlara dokunmuyordu - yani "SEO" bu turdan önce iki yönden de tamamen boştu. Düzeltildi:
`CreateProductCommand`/`UpdateProductCommand`/`CreateCategoryCommand`/`UpdateCategoryCommand`'a
opsiyonel Meta parametreleri eklendi, Admin'de Ürün Düzenle ve Kategori formuna "Meta Başlık/
Açıklama/Anahtar Kelime" alanları kondu; Storefront `_Layout.cshtml`'e `<meta name="description">`/
`<meta name="keywords">` (yalnızca doluysa render edilir) eklendi, Ürün Detayı ve Kategori sayfaları
`ViewData["MetaDescription"/"MetaKeywords"]`'i dolduruyor ve sayfa `<title>`'ı boşsa ürün/kategori
adına, doluysa Meta Başlığa düşüyor.

Gerçek SQL Server'a karşı bir entegrasyon testiyle (arama metninin ada VE açıklamaya göre eşleştiği,
fiyat aralığı filtresinin doğru ürünleri döndürdüğü, eşleşmeyen bir aramanın boş liste verdiği) VE
çalışan Storefront'a karşı canlı HTTP akışıyla (arama sonucu doğru ürünü buluyor, fiyat aralığı
dışındaki ürün gizleniyor, ürün/kategori sayfalarının `<title>`/meta description/meta keywords
etiketlerinin Admin'de girilen Meta alanlarını doğru yansıttığı) doğrulandı.

**Kapsam dışı (bilinçli, ayrı bir iş):** Çok dil (TR/EN/DE/FR/NL/ES/AR) arayüz desteği ve Arapça RTL
- `Program.cs`'teki "TODO Faz 3" notu hâlâ duruyor, tüm Storefront controller'ları hâlâ `"tr"`
sabit kullanıyor. Çok para birimi gösterimi de eklenmedi (her şey TRY sabit). CMS/blog sayfaları yok.

## Ödeme Sağlayıcı Kimlik Bilgileri — Nereye Girilir?

Gerçek bir ödeme sağlayıcısı (iyzico/PayTR/Param/PayPal/Stripe) hesabınız olduğunda, kod değişikliği
GEREKMEDEN Admin → **Entegrasyonlar** (`/integrations`) → **Ödeme** sekmesinden ilgili karta
tıklayıp API anahtarlarınızı girebilirsiniz - bu dinamik, metadata-güdümlü ekran zaten Faz 1'de
kurulmuştu ve bu turda 6 ödeme sağlayıcısının tümü için canlı doğrulandı. "Kaydet ve Bağlantıyı Test
Et" butonuna basınca alanların dolu olup olmadığı kontrol edilir ve sağlayıcı Aktif olur - o andan
itibaren Storefront checkout'ta müşteriye seçenek olarak görünür.

**Önemli sınır (şeffaf olmak için belirtiliyor):** Şu an bu 5 gerçek sağlayıcının (`iyzico`, `paytr`,
`param`, `paypal`, `stripe`) `AuthorizeAsync`/`CaptureAsync`/`RefundAsync` metodları HÂLÂ STUB'DIR -
girilen kimlik bilgileri saklanır/şifrelenir ve "bağlantı testi" yalnızca zorunlu alanların dolu
olup olmadığını kontrol eder (gerçek bir HTTP çağrısı yapmaz), ödeme onaylama da gerçek bir API
çağrısı yapmadan her zaman "başarılı" döner. Yani ekran ve akış tamamen hazır ve çalışıyor, ama
gerçek kart tahsilatı yapmaz. Gerçek bir kartla test etmek/canlıya almak için her sağlayıcının kendi
API dokümantasyonuna göre `Dekorras.Infrastructure/PaymentProviders/PaymentProviders.cs` içindeki
ilgili sınıfın üç metodunun gerçek HTTP çağrılarıyla değiştirilmesi gerekir - bu, sandbox kimlik
bilgileri olmadan güvenilir şekilde yazılıp test edilemeyeceği için bu oturumda yapılmadı (yalnızca
`bank-transfer` zaten "gerçek" bir akış - firma kendi banka hesabına gelen havaleyi elle onaylıyor,
otomatik API çağrısı gerektirmiyor).

## Kampanya Motoru (Faz 2 devamı)

Kupondan farkı: müşteri bir kod GİRMEZ. Admin (`/ecommerce/campaigns`) bir başlangıç/bitiş tarihi,
indirim türü (yüzde/sabit tutar) ve isteğe bağlı bir "Min. Sepet Tutarı" koşuluyla bir kampanya
tanımlar; koşulu sağlayan HER sepete `PlaceOrderCommand` içinde checkout anında otomatik uygulanır.

- **Kupon ile karşılıklı dışlama (bilinçli tasarım kararı):** Bir checkout'ta zaten bir kupon
  uygulanmışsa (`Cart.CouponCode` doluysa) kampanya motoru HİÇ değerlendirilmez - `Order.CampaignId`
  ve `Order.CouponCode` aynı anda dolu olamaz, ikisi de aynı `DiscountTotalTry` alanına doğrudan
  YAZAR (toplanmaz). Kupon+kampanya birlikte "üst üste binme" (stacking) mantığı ayrı bir iş kararı
  gerektirdiği için bu turda bilinçli olarak kapsam dışı bırakıldı.
- **En iyi kampanya seçimi:** Aynı anda birden çok kampanya koşulu sağlıyorsa (`Campaign.MeetsRules` +
  `Campaign.IsValidNow`), en yüksek TL indirimi hesaplayan (`Campaign.CalculateDiscount`) seçilir.
- **Kural motoru şu an yalnızca "MinCartTotal" değerlendirir.** `Campaign.Rules` (`PromotionRule`
  entity'si, `RuleType`/`RuleValueJson` alanlarıyla) ileride "CategoryId", "CustomerGroup" gibi başka
  kural türlerine genişletilebilecek şekilde tasarlandı, ama bu turda YALNIZCA `MinCartTotal` kuralı
  `Campaign.MeetsRules` içinde okunuyor - başka bir `RuleType` eklense bile henüz hiçbir etkisi olmaz.
- Admin ekranı (`CampaignList.razor`): liste + oluşturma formu + Aktifleştir/Pasife Al - `CouponList.razor`
  ile birebir aynı desen.
- **Sepet önizlemesi:** `GetCartQuery`/`/Cart` VE `/Checkout` sayfaları da kupon uygulanmamışsa aynı
  "en iyi kampanyayı bul" mantığını (salt-okunur, hiçbir şeyi kalıcı DEĞİŞTİRMEDEN) çalıştırıp
  `CartDto.CampaignName`/`DiscountTry` alanlarıyla önizleme gösterir - müşteri checkout'u
  tamamlamadan ÖNCE "Kampanya İndirimi (Ad)" satırını ve indirimli toplamı görür. Bu yalnızca bir
  ÖNİZLEME: gerçek indirim ve kullanım sayacı artışı hâlâ yalnızca `PlaceOrderCommand` checkout
  TAMAMLANDIĞINDA aynı mantığı bağımsız olarak tekrar çalıştırıp uygular (sepetteki ürünler/tutar
  checkout'a kadar değişebileceği için önizleme ile gerçek uygulama arasında bilinçli bir kod
  tekrarı var - iki ayrı zamanlama, aynı iş kuralı).
- Regresyon testleri: `FullCheckoutFlowTests.KosuluSaglayanSepete_EnYuksekIndirimliKampanyaOtomatikUygulanirVeKullanimSayaciniArtirir`
  (en iyi kampanya seçimi + kullanım sayacı), `...KuponUygulanmisSepette_UygunKampanyaOlsaBileAtlanir`
  (karşılıklı dışlama) ve `...SepetOnizlemesi_UygunKampanyaVarsaCheckoutTamamlanmadanIndirimiOnizlerVeKuponVarsaGostermez`
  (sepet önizlemesi). Gerçek Storefront (`/Cart`, checkout formu) + Admin (`/ecommerce/campaigns`
  listesi) süreçlerine karşı canlı HTTP ile de doğrulandı: %20'lik bir kampanya 1.000 TRY'lik sepette
  200 TRY indirim üretti, `Order.CampaignId` doğru sipariş ID'siyle eşleşti, kampanya kullanım sayacı
  arttı ve Admin listesinde "Kullanım: 1" olarak göründü; ayrıca `/Cart` sayfası checkout'a hiç
  gidilmeden %15'lik bir kampanyayı doğru şekilde önizledi (1.000 ₺ → 850 ₺).

## Ürün Ağırlığı (Kargo Hesabı için)

`Product.Weight`/`WeightUnit` Faz 0/1'den beri domain'de vardı ama Admin'de hiçbir yerden
ayarlanamıyordu - `PlaceOrderCommand` bu yüzden ağırlıksız HER ürün için varsayılan 1 kg
kullanıyordu (`DefaultItemWeightKg`), bu da kargo ücretini gerçek dışı hesaplıyordu. Eklenenler:
`Product.SetWeight(decimal? weightKg)` (yalnızca ağırlığı ayarlar, Length/Width/Height'a dokunmaz -
onlar hâlâ hiçbir yerden yazılamıyor, kapsam dışı), `CreateProductCommand`/`UpdateProductCommand`'a
opsiyonel `WeightKg` parametresi, `/ecommerce/products/{id}` formuna "Ağırlık (kg)" alanı (boş
bırakılırsa varsayılan 1 kg kullanılmaya devam eder - geriye dönük uyumlu). Regresyon testiyle
(3 kg'lık bir üründen 2 adet → kargo 2×3×15=90 TRY, varsayılan 1kg kullanılsaydı 30 TRY olurdu) VE
çalışan Admin'e karşı canlı HTTP akışıyla (ürün formunda "7.5000" olarak doğru göründüğü) doğrulandı.

**Bu turda ayrıca bulunup düzeltilen KENDİ script hatam (koda değil):** Önceki iki turun (kampanya
motoru + sepet önizlemesi) smoke-test temizlik SQL'leri `Products.Sku` sütununu hedefliyordu, ama
`CreateProductCommand` o alanı gerçekte `ProductCode` sütununa yazıyor - bu TAM OLARAK devamı 12'de
bir kez daha keşfedilip not edilmiş olan hata, yine tekrarlanmış. Sonuç: `SMKKAMP-001`/
`SMKKAMPONIZ-001` test ürünleri dev DB'de sessizce kalmıştı. Bu turda fark edilip düzeltildi (doğru
sütunla temizlendi) - **ders: bu proje için temizlik script'i yazarken KESİNLİKLE `ProductCode`
kullan, `Sku` DEĞİL; her smoke test sonrası "gerçekten silindi mi" diye ayrıca doğrulamak değerli.**

## Ürün Değerlendirmeleri ve Soru-Cevap

`Domain.Catalog.ProductReview`/`ProductQuestion` Faz 0/1'den beri vardı ama hiç Application/UI
katmanı yoktu (yine tanıdık orphaned-entity deseni). Eklenenler:

- **Storefront** (`/Product/Details`): giriş yapmış müşteriler bir puan (1-5) + yorum göndererek
  ürünü değerlendirebilir veya bir soru sorabilir. Ürünün ortalama puanı (yalnızca ONAYLANMIŞ
  değerlendirmeler üzerinden, `GetProductBySlugQuery` içinde EF `Average()` ile) yıldız gösterimiyle
  başlıkta görünür. Aynı müşteri aynı ürünü İKİNCİ kez değerlendiremez. Giriş yapmamış ziyaretçiye
  "Giriş yapın" bağlantısı gösterilir.
- **Admin** (`/ecommerce/reviews`): tek bir sayfada iki kuyruk - "Onay Bekleyen Değerlendirmeler"
  (Onayla/Reddet) ve "Yanıt Bekleyen Sorular" (yanıt kutusu + Yanıtla). Onaylanan değerlendirme
  Storefront'ta hemen görünür; yanıtlanan soru da aynı şekilde.
- **Bulunan gerçek hata (koda, script'e değil):** `ProductReview.IsApproved` yalnızca İKİ durumludur
  (true/false) - domain'deki mevcut `Reject()` metodu `IsApproved = false` yapıyordu ki bu, HENÜZ
  hiç işlenmemiş bekleyen bir değerlendirmeyle AYNI durum. Sonuç: bir değerlendirme reddedilse bile
  moderasyon kuyruğunda "bekliyor" olarak SONSUZA KADAR görünmeye devam ederdi (regresyon testiyle
  yakalandı). Domain'e üçüncü bir durum eklemek yerine (kapsamı gereksiz genişletir), reddetme artık
  kaydı KALICI OLARAK SİLİYOR (`RejectProductReviewCommand` → `IRepository<T>.Remove`) - moderasyon
  reddi = kaydı at. Kullanılmayan `ProductReview.Reject()` domain metodu kaldırıldı.
- Regresyon testleri (`ProductFeedbackRegressionTests`: onay öncesi/sonrası görünürlük, ortalama puan
  hesabı, aynı müşterinin ikinci değerlendirmesinin reddi, reddedilen kaydın kuyruktan gerçekten
  kaybolması, soru-cevap akışı) VE gerçek Storefront (kayıt→giriş→değerlendirme/soru gönder) + Admin
  (`/ecommerce/reviews` → onayla/yanıtla) süreçlerine karşı canlı HTTP ile doğrulandı - onaylanan
  değerlendirme yıldızlarıyla, yanıtlanan soru cevabıyla birlikte Storefront'ta gerçekten göründü.
  Toplam 52 test (13+3+36).

## Ürün Özellikleri (Spesifikasyonlar)

`Domain.Catalog.ProductAttribute`/`ProductAttributeValue` Faz 0/1'den beri vardı - hatta `Product`
aggregate'inin kendisinde `AddAttributeValue`/`AttributeValues` navigasyonu ve EF konfigürasyonu
ZATEN kuruluydu (`ConfigureOwnedCollection` ile), ama Application/Admin/Storefront katmanlarının
HİÇBİRİ bunu hiç kullanmıyordu. Varyanttan farkı: fiyatı/stoku ETKİLEMEZ, yalnızca bilgi amaçlı bir
spesifikasyon tablosudur (ör. "Malzeme: Pamuk", "Renk: Kırmızı").

- `ProductAttribute` TÜM ürünler arasında paylaşılan global bir özellik TÜRÜ tanımıdır (`Name`
  üzerinde benzersiz dizin zaten vardı) - Admin `/ecommerce/products/{id}` sayfasından yeni bir tür
  oluşturulabilir (ör. "Malzeme").
- Bir ürün+özellik türü çifti için TEK bir değer kuralı, yeni bir migration/veritabanı kısıtı
  gerektirmeden uygulama katmanında upsert olarak uygulanır (`SetProductAttributeValueCommand` -
  aynı türe ikinci kez değer girilirse yeni satır eklemez, mevcut günceller).
- Storefront `/Product/Details` sayfası, değer girilmiş ürünlerde açıklamanın altında bir
  spesifikasyon tablosu gösterir.
- **Bulunan EF LINQ çevirisi sorunu (bu oturumda tekrar eden bir sınıf hata):** `GetProductAttributeValuesQuery`
  başta `.Join(...).OrderBy(v => v.AttributeName)` şeklindeydi - EF Core, yeni oluşturulmuş bir DTO
  (record) özelliğine göre sıralamayı SQL'e çeviremedi (`decimal.TryParse`'ın çevrilememesiyle aynı
  sınıf sorun, bkz. Kampanya Motoru bölümü). Düzeltme: `.ToList()` ile materyalize ettikten SONRA
  bellekte sıralama yapılıyor.
- **Razor tuzağı:** `@foreach (var attribute in ...)` yazıldığında Razor, `@attribute.Name` ifadesini
  `@attribute` DİREKTİFİ (component/route meta verisi) sanıp derleme hatası veriyor - "attribute"
  kelimesi Razor'da ayrılmış bir direktif adı. Döngü değişkeni `attributeType`/`spec` gibi çakışmayan
  bir isimle değiştirilerek düzeltildi. **Ders: Razor dosyalarında döngü değişkeni adı olarak
  `page`, `attribute`, `inject`, `namespace`, `model` gibi direktif kelimelerini KULLANMA.**
- 3 yeni regresyon testi (upsert/güncelleme, silme, aynı isimli özellik türünün ikinci kez
  oluşturulamaması) VE gerçek Storefront + Admin süreçlerine karşı canlı HTTP ile doğrulandı.
  Toplam 55 test (13+3+39).

## Ürün Videoları

`Domain.Catalog.ProductVideo` ve `Product.AddVideo`/`Videos` navigasyonu (EF konfigürasyonu dahil)
Faz 0/1'den beri hazırdı, yalnızca Application/Admin/Storefront katmanları eksikti - bu oturumdaki
son orphaned-entity kapanışı. Admin `/ecommerce/products/{id}`'e "Videolar" bölümü (bağlantı+opsiyonel
başlık ekle/sil), Storefront `/Product/Details`'e "Ürün Videoları" bağlantı listesi eklendi
(iframe GÖMÜLMEDİ - rastgele kullanıcı girdisi URL'sini iframe'e gömmek XSS/clickjacking riski
taşır, bu yüzden bilinçli olarak yalnızca `target="_blank" rel="noopener"` bağlantı kullanıldı).
1 yeni regresyon testi + gerçek Admin/Storefront süreçlerine karşı canlı HTTP ile doğrulandı.
Toplam 56 test (13+3+40).

## Çok Dil / RTL Alt Yapısı (Faz 3 devamı)

Hedef diller plan §7'ye göre **TR, EN, DE, FR, NL, ES, AR** (Arapça RTL) - bu turda mekanizmanın
TAMAMI kuruldu ve **TR/EN/AR** ile uçtan uca canlı doğrulandı; DE/FR/NL/ES mekanizmaya ZATEN
açıktır, yalnızca veri girişi (Admin'de çeviri) ve (istenirse) UI metni çevirisi meselesidir.

**Dil seçimi ve içerik (Storefront):**
- `StorefrontLanguage` (`Dekorras.Storefront`), müşterinin seçtiği dili `dekorras_lang` çerezinde
  tutar - `GetLanguage(HttpContext)`/`SetLanguage(HttpContext, code)`. `/Language/SetLanguage`
  (POST) dil değiştirip geldiği sayfaya geri döner.
- TÜM Storefront controller'ları (Home/Product/Category/Search/Cart/Checkout) ve
  `MainMenuViewComponent`, ürün/kategori sorgularına artık hardcoded `"tr"` DEĞİL
  `StorefrontLanguage.GetLanguage(HttpContext)` geçiyor - `ProductTranslation`/`CategoryTranslation`
  zaten Faz 0/1'den beri herhangi bir `LanguageCode`'u destekliyordu, yalnızca Storefront'un okuma
  tarafı sabitti.
- `Program.cs`'te bir middleware, her istekte çerezdeki dile göre `CultureInfo.CurrentCulture`/
  `CurrentUICulture`'ı ayarlar - hem sayı/tarih biçimlendirmesi (ör. `ToString("N2")`) hem de
  aşağıdaki .resx tabanlı UI metni yerelleştirmesi bunu kullanır.

**UI metni yerelleştirmesi (.resx + IStringLocalizer):**
- `SharedResource` işaretçi sınıfı + `Resources/SharedResource.en.resx` / `.ar.resx`. Anahtar
  YOK - Türkçe metnin kendisi anahtar olarak kullanılıyor, bu yüzden `tr` için ayrı bir .resx
  gerekmiyor (eşleşme yoksa anahtar - zaten Türkçe - olduğu gibi döner). `_ViewImports.cshtml`
  `IStringLocalizer<SharedResource> Localizer`'ı TÜM view'lara enjekte ediyor.
- **Bu turda gerçekten çevrilen (kanıtlanmış) kapsam:** `_Layout.cshtml` (nav/footer/dil seçici),
  `MainMenu` bileşeni, `_ProductGrid.cshtml` (Anasayfa/Kategori/Arama'da paylaşılan), `Home/Index`,
  `Cart/Index`, ve dört sayfadaki "Anasayfa" breadcrumb'ı. **Bilinçli olarak kapsam dışı bırakılan:**
  Checkout formundaki alan etiketleri, Product/Details sayfasının kendi metinleri (Sepete Ekle,
  Değerlendirmeler, vb.), Admin (Blazor - plan zaten "Admin TR kalabilir, RTL yalnızca müşteri
  yüzü için gerekli" diyor) - bunlar hâlâ yalnızca Türkçe, yeni bir .resx anahtarı eklemek kadar
  basit ama bu turda YAPILMADI.

**Arapça RTL:**
- `_Layout.cshtml`, seçili dil Arapça olduğunda `<html dir="rtl">` yazar VE
  `bootstrap.rtl.min.css`'i yükler (normal derlemenin YERİNE, ikisi birden değil) - Bootstrap'ın
  hazır RTL derlemesi zaten `wwwroot/lib/bootstrap`'ta mevcuttu, ek bir paket kurulumu gerekmedi.
  Canlı HTTP ile `dir="rtl"` + RTL CSS'in doğru yüklendiği ve tüm nav/sayfa metninin Arapçaya
  gerçekten döndüğü doğrulandı.

**Admin'de çok dilli çeviri girişi (gerçek bir eksiklik kapatıldı):**
- `SupportedLanguages.All` (`Dekorras.Application.Common`) 7 dilin tam listesini taşır - hem
  Storefront'un (yalnızca UI çevirisi olan 3 dille filtrelenmiş) dil seçicisi hem de Admin'in
  çeviri ekranları buradan besleniyor.
- `/ecommerce/products/{id}` ve `/ecommerce/categories`'e "Düzenlenen Dil" seçici eklendi - daha
  önce Admin'de `LanguageCode` HER ZAMAN sabit `"tr"` idi, yani `CreateProductCommand`/
  `UpdateProductCommand`'ın zaten kabul ettiği `LanguageCode` parametresi Admin'den ASLA başka bir
  değerle çağrılamıyordu (Storefront/API teorik olarak `en` bir çeviri gösterebilirdi ama böyle bir
  çeviriyi girecek hiçbir ekran yoktu). Artık admin dili değiştirip o dilde Ad/Açıklama/Meta
  girebiliyor - `Product.SetTranslation`/`Category.SetTranslation` zaten dil bazlı upsert yapıyordu
  (bir dile yazmak diğerini SİLMEZ), yalnızca Admin UI'ın bunu tetiklemesi eksikti.

**Regresyon testleri** (`MultiLanguageRegressionTests`): aynı ürüne/kategoriye TR+EN+AR çevirisi
eklenip üçünün de doğru döndüğü, çevirisi olmayan bir dilin (DE) ürün koduna/slug'a düştüğü
doğrulandı. **Gerçek Storefront + Admin süreçlerine karşı canlı HTTP ile de doğrulandı:** TR→EN→AR
geçişinde nav/footer metni, ürün adı/açıklaması, `dir`/`lang` özniteliği ve RTL CSS'in hepsi doğru
değişti; Admin'de her iki dil seçicisinin 7 dili doğru listelediği ve seçili ürünün Türkçe adını
doğru gösterdiği görüldü. Toplam 58 test (13+3+42).

## Hediye Çeki

`Domain.Ordering.GiftVoucher` Faz 0/1'den beri vardı (`Redeem` metodu dahil) ama hiç Application/
Admin/Storefront katmanı yoktu. Kupon'dan temel farkı: **bir indirim değil, önceden ödenmiş bir
BAKİYEDİR** - checkout'ta ödemenin bir kısmını (veya tamamını) karşılar, kalan bakiye varsa bir
SONRAKİ siparişte tekrar kullanılabilir. Bu yüzden Kupon/Kampanya'nın aksine **kuponla VEYA
kampanyayla BİRLİKTE kullanılabilir** - `Order`/`Cart`'ta ayrı bir `GiftVoucherCode`/
`GiftVoucherAmountAppliedTry` alanı taşır, `DiscountTotalTry`'a KARIŞMAZ (`GrandTotal = SubTotal +
Tax + Shipping - Discount - GiftVoucherAmountApplied`).

- Admin `/ecommerce/gift-vouchers`: kod + tutar ile oluşturma, aktif/pasif etme, kalan bakiyeyi
  görüntüleme.
- Storefront `/Cart`: kupon kutusunun yanına "Hediye Çeki Kodu" kutusu - uygulanan tutar (bakiyeyi
  AŞMAYAN, sepetin o anki - kargo hariç - tutarını AŞMAYAN, ikisinin küçüğü) önizlenir; `/Checkout`
  özetinde de gösterilir.
- `PlaceOrderCommand` checkout ANINDA gerçek uygulanacak tutarı (`Math.Min(kalan bakiye, o anki
  GrandTotal - kargo DAHİL)`) hesaplayıp hem `Order`'a işler hem `GiftVoucher.Redeem()` ile bakiyeden
  düşer - ödeme ağ geçidine gönderilen tutar (`order.GrandTotalTry`) bu düşümü zaten yansıtır, yani
  gerçek bir ödeme sisteminde müşteriden yalnızca KALAN tutar tahsil edilirdi.
- Regresyon testleri (`FullCheckoutFlowTests`): hediye çekinin kupon ile BİRLİKTE uygulanabildiği,
  sepet tutarından BÜYÜK bir çekin yalnızca sipariş tutarı kadarını düşüp kalanını bir SONRAKİ
  sipariş için sakladığı. Gerçek Storefront (sepete ekle → hediye çeki uygula → checkout tamamla) +
  Admin (`/ecommerce/gift-vouchers` listesinde kalan bakiyenin 0'a düştüğü) süreçlerine karşı canlı
  HTTP ile de doğrulandı. Toplam 60 test (13+3+44).

## Ürün Fiziksel Bilgileri ve GTİP/HS Kodu

Plan §7'nin ürün modeli tanımı "boyut/ağırlık" ve "HS/GTİP kodu (e-ihracat)" alanlarını açıkça
listeler (`Product.Length`/`Width`/`Height`/`HsCode` Faz 0/1'den beri domain'de vardı, `SetHsCode`
metodu bile hazırdı) ama Ağırlık dışında (bkz. yukarıdaki "Ürün Ağırlığı") hiçbiri Application/Admin
katmanına hiç bağlanmamıştı. Eklenenler:

- `Product.SetPackageDimensions(decimal? lengthCm, decimal? widthCm, decimal? heightCm)` - mevcut
  geniş kapsamlı `SetDimensions` metodunun YERİNE bilinçli olarak dar bir metot: `Weight`/`WeightUnit`'e
  KESİNLİKLE dokunmaz (aksi halde ayrı ayrı düzenlenebilen iki alan grubu birbirini ezerdi - `SetWeight`
  ile aynı gerekçe).
- `CreateProductCommand`/`UpdateProductCommand`'a opsiyonel `HsCode`/`LengthCm`/`WidthCm`/`HeightCm`.
- `/ecommerce/products/{id}` formuna Uzunluk/Genişlik/Yükseklik (cm) + "GTİP / HS Kodu (e-ihracat)"
  alanları eklendi.
- **Bilinçli olarak yapılmayan:** Bu alanlar şu an yalnızca BİLGİ AMAÇLIDIR - `PlaceOrderCommand`'ın
  kargo maliyet hesabı hâlâ yalnızca `Weight`'i kullanıyor (yurt içi kargo sağlayıcıları desi/hacim
  hesabı yapmıyor), gerçek bir gümrük beyannamesi/proforma fatura üretimi de bu turun kapsamı dışında
  - plan §6'nın "e-ihracat detayları" fazına ait, ayrı bir iş.
- 1 yeni regresyon testi (boyutların ağırlığı EZMEDİĞİ, ikisinin bağımsız güncellenebildiği) VE
  gerçek Admin sürecine karşı canlı HTTP ile doğrulandı. Toplam 61 test (13+3+45).

## Ürün Tanımlayıcıları (SKU/UPC/EAN/JAN/ISBN/MPN) ve Meta Robots

`Product.SetIdentifiers` Faz 0/1'den beri hazırdı (SKU/UPC/EAN/JAN/ISBN/MPN barkod/tanımlayıcı
alanları) ama hiç Application/Admin katmanı yoktu - `MetaRobots` alanının ise domain'de setter'ı
BİLE yoktu. İkisi de kapatıldı:

- `Product.SetMetaRobots(string?)` eklendi - Storefront'un `_Layout.cshtml`'i artık `MetaTitle`/
  `MetaDescription`/`MetaKeywords` ile aynı desende, doluysa `<meta name="robots">` etiketini render
  ediyor (ör. bir ürün "noindex,nofollow" olarak işaretlenirse arama motorları onu dizinlemez).
- `CreateProductCommand`/`UpdateProductCommand`'a `Sku`/`Upc`/`Ean`/`Jan`/`Isbn`/`Mpn`/`MetaRobots`
  eklendi, Admin formuna karşılık gelen alanlar + Meta Robots dropdown'ı (`index,follow`/
  `noindex,follow`/`index,nofollow`/`noindex,nofollow`) kondu.
- **Bilinçli olarak yapılmayan:** Bu tanımlayıcılar şu an yalnızca SAKLANIYOR - hiçbir barkod
  tarayıcı/marketplace feed entegrasyonu (Google Merchant Center vb.) bunları TÜKETMİYOR; bu
  alanların asıl değeri gelecekte böyle bir entegrasyon eklendiğinde ortaya çıkacak.
  `Product.GoogleMerchantAgeGroup`/`GoogleMerchantGender` ise BİLİNÇLİ OLARAK dokunulmadı - bunlar
  yalnızca gerçek bir Google Shopping feed export özelliğiyle ANLAM kazanır, böyle bir özellik bu
  sistemde yok ve icat edilmedi (var olmayan bir entegrasyon için alan doldurmak amaçsız olurdu).
- 1 yeni regresyon testi + gerçek Admin (form alanlarının doğru göründüğü) + Storefront
  (`<meta name="robots">` etiketinin doğru render edildiği) süreçlerine karşı canlı HTTP ile
  doğrulandı. Toplam 62 test (13+3+46).

## Favoriler (Wishlist) ve Bülten Aboneliği

`Customer.AddToWishlist`/`RemoveFromWishlist`/`Wishlist` ve `SubscribeNewsletter`/
`UnsubscribeNewsletter`/`NewsletterSubscribed` Faz 0/1'den beri domain'de tamamen hazırdı (EF
konfigürasyonu dahil) ama hiç Application/UI katmanı yoktu - klasik e-ticaret "favorilere ekle"
özelliği.

- Storefront `/Product/Details`: giriş yapmış müşteriler "♡ Favorilere Ekle" / "♥ Favorilerden Çıkar"
  düğmesiyle bir ürünü favoriye ekleyip çıkarabilir (aynı ürünü ikinci kez eklemek yinelenen satır
  OLUŞTURMAZ - `Customer.AddToWishlist` zaten var olup olmadığını kontrol ediyordu).
- `/Account/Wishlist` ("Favorilerim" - nav'a eklendi): favori ürünlerin listesi + kaldırma düğmesi,
  üstte bir "Bülten Aboneliği" aç/kapa anahtarı (`GetMyCustomerProfileQuery`'e `NewsletterSubscribed`
  eklendi).
- Regresyon testleri (`WishlistAndNewsletterRegressionTests`: ikinci eklemenin yinelenmediği,
  kaldırmanın çalıştığı, abonelik durumunun değiştirilebildiği) VE gerçek Storefront süreçlerine
  karşı canlı HTTP ile doğrulandı (kayıt→giriş→favorilere ekle→ürün sayfasında buton durumu
  değişti→Favorilerim'de göründü→bültene abone ol→durum güncellendi). Toplam 64 test (13+3+48).

## Gerçek İade (Refund) Akışı

Sipariş durum makinesinde `Refunded` (İade Edildi) durumu Faz 0/1'den beri vardı ve
`Shipped`/`Completed`'ten erişilebiliyordu, ama admin bu duruma geçirdiğinde `TransitionOrderStatusCommand`
yalnızca durum ETİKETİNİ değiştiriyordu - `IPaymentGateway.RefundAsync` (arayüzde ve 6 sağlayıcı
stub'ında da ZATEN vardı) hiçbir zaman çağrılmıyordu, `Payment.Status` sonsuza kadar `Authorized`
kalıyordu. Yani bir admin bir siparişi "İade Edildi" işaretleyebiliyordu ama sistemde GERÇEKTEN hiçbir
iade işlemi kaydı oluşmuyordu.

- Yeni `RefundOrderCommand`: siparişin `Payment`ini bulur, orijinal başarılı işlemi (Authorization/
  Capture) bulur, ilgili sağlayıcının `RefundAsync`'ini GERÇEKTEN çağırır, sonucu `Payment`e yeni bir
  `Refund` işlemi olarak kaydeder (`Payment.Status` otomatik `Refunded`'a döner) ve YALNIZCA başarılıysa
  siparişi `Refunded`'a geçirir.
- `/ecommerce/orders/{id}` sayfasında hedef durum `Refunded` olduğunda (Shipped/Completed'ten)
  artık genel "durum değiştir" düğmesi YERİNE özel bir "İade Et" düğmesi bu komutu tetikler
  (`Shipped` hedefinin kargo takip no istemesiyle AYNI desen).
- Zaten iade edilmiş/hiç ödemesi olmayan bir sipariş TEKRAR iade edilmeye çalışılamaz; başarısız bir
  iade denemesi de (gerçek sağlayıcı reddederse) denetim izi olarak kaydedilir, sipariş durumu
  DEĞİŞMEZ.
- **Kapsam dışı bırakılan (bilinçli):** `Cancelled` durumuna geçişin ödeme tarafını nasıl etkilemesi
  gerektiği (henüz sevk edilmemiş bir siparişte muhtemelen VOID, sevk edilmiş bir siparişte muhtemelen
  REFUND) ayrı bir iş kararı gerektiriyor - bu turda yalnızca açıkça "İade Edildi" durumuna GERÇEK bir
  iade bağlandı.
- 1 yeni regresyon testi (gerçek iade tetiklenmesi + tekrar iade edilememe) VE gerçek Admin sürecine
  karşı canlı HTTP ile doğrulandı ("İade Et" düğmesi tetiklenince hem sipariş rozetinin "İade Edildi"
  hem ödeme rozetinin "Refunded" olarak güncellendiği görüldü). Toplam 65 test (13+3+49).

## Entegrasyon Bağlantı Geçmişi ve Yeniden Test

`Domain.Integrations.IntegrationHealthCheckLog` Faz 0/1'den beri EF'e kayıtlıydı ama hiç
kullanılmıyordu - `IntegrationProvider` yalnızca SON kontrolün zamanını/mesajını tutuyordu, önceki
kontrollerin hiçbir izi kalmıyordu. Ayrıca bir sağlayıcının bağlantısını doğrulamanın TEK yolu
formu (şifreler dahil) baştan doldurup kaydetmekti - "hâlâ çalışıyor mu?" diye kontrol etmenin ayrı,
hafif bir yolu yoktu.

- Her `ConfigureIntegrationProviderCommand` çağrısı artık bir `IntegrationHealthCheckLog` satırı
  ekliyor (önceki geçmişi SİLMEDEN).
- Yeni `TestIntegrationProviderConnectionCommand`: yalnızca sağlayıcı ID'siyle çalışır, TÜM formu
  yeniden doldurmaya gerek kalmadan mevcut (şifresi çözülmüş) yapılandırma değerleriyle bağlantıyı
  yeniden test eder.
- `/integrations` sayfasına "Yeniden Test Et" düğmesi (zaten en az bir kez yapılandırılmış her kart
  için) + "Geçmiş" bağlantısı (son 10 kontrolü tarih/başarı/mesaj olarak listeler) eklendi.
- 1 yeni regresyon testi (yapılandırma + yeniden test → 2 ayrı log satırı, biri diğerini SİLMEZ) VE
  gerçek Admin sürecine karşı canlı HTTP ile doğrulandı (bu oturumun ÖNCEDEN yapılandırdığı gerçek
  `bank-transfer` sağlayıcısı yeniden test edilip "Son kontrol" zaman damgasının GERÇEKTEN
  güncellendiği, log sayısının 0'dan 1'e çıktığı görüldü). Toplam 66 test (13+3+50).

## CMS Sayfaları (Bilgi Sayfaları)

Plan §4 açıkça 6 statik kurumsal/yasal sayfa istiyor (Gizlilik Politikası, Hakkımızda, İptal ve
İade Koşulları, Mesafeli Satış Sözleşmesi, Ön Bilgilendirme Formu, Satış Sözleşmesi) ve
"mevcut yasal metinlerin CMS modülüne taşınmasını" söylüyor. `Domain.Content.CmsPage`/
`CmsPageTranslation` Faz 0/1'den beri hazırdı ama hiç Application/UI katmanı yoktu -
**Storefront'un footer'ındaki "Gizlilik Politikası" bağlantısı bu turdan ÖNCE ASP.NET'in scaffold
placeholder metnine gidiyordu** ("Use this page to detail your site's privacy policy." - gerçek bir
üretim sitesinde görünmemesi gereken bir metin).

- Admin `/cms/pages`: sayfa oluşturma/düzenleme/aktif-pasif etme, dil seçici (7 dil - `CategoryList`
  ile aynı desen), her sayfanın kendi Meta Başlık/Açıklama alanı.
- Storefront `/sayfa/{slug}`: yalnızca Aktif bir sayfa döner (Product/Category'deki aynı desen).
  Footer'daki "Gizlilik Politikası" artık GERÇEK bir CMS sayfasına gidiyor; eski placeholder
  `HomeController.Privacy()` action'ı ve view'ı tamamen SİLİNDİ (artık hiçbir yerden linklenmiyordu).
- **Bilinçli olarak yapılmayan (önemli, açıkça belirtiliyor):** Bu turda GERÇEK, hukuken geçerli
  yasal metin YAZILMADI - yalnızca mekanizma kuruldu. KVKK/Mesafeli Satış Yönetmeliği'ne uygun
  gerçek içerik hukuki incelemeden geçmesi gereken, bu oturumun uydurmaması gereken bir metin
  türüdür; Admin ekranından gerçek içerik girilmesi gerekiyor. Aynı şekilde `BlogPost`/`Banner`/
  `MenuItem` (aynı `Content` bounded context'inde) hâlâ tamamen orphaned - bu turun kapsamı yalnızca
  footer'daki gerçek/görünür boşluğu (CmsPage) kapatmaktı.
- 2 yeni regresyon testi (çok dilli çeviri upsert'i, pasif sayfanın Storefront'ta 404 vermesi ama
  Admin'de hâlâ düzenlenebilir kalması, aynı slug'ın ikinci kez kullanılamaması) VE gerçek
  Admin/Storefront süreçlerine karşı canlı HTTP ile doğrulandı (footer linki değişti, gerçek sayfa
  başlık+içerik+meta açıklamayla render edildi, Admin listesinde doğru göründü). Toplam 68 test
  (13+3+52).

## Ana Sayfa Bannerları

`Domain.Content.Banner` Faz 0/1'den beri vardı ama `Activate`/`Deactivate`/`UpdateDetails` domain
metotları BİLE yoktu (yalnızca oluşturma), Application/UI katmanı da hiç yoktu.

- Admin `/cms/banners`: görsel yükleme (ürün görselleriyle AYNI `IFileStorage`/paylaşılan
  `uploads` klasörü mekanizması), bağlantı URL'si + gösterim sırası, aktif/pasif etme, silme
  (dosyayı da GERÇEKTEN diskten siler).
- Storefront anasayfası: Bootstrap carousel'i ile yalnızca Aktif banner'ları gösterim sırasına göre
  gösterir - banner yoksa carousel hiç render edilmez.
- 1 regresyon testi (aktif/pasif filtreleme + sıralama) VE gerçek dosya yükleme dahil Admin/
  Storefront süreçlerine karşı canlı HTTP ile doğrulandı (gerçek bir PNG yüklendi, anasayfada
  `<img>` etiketinin GERÇEKTEN o dosyayı sunduğu, silme sonrası dosyanın diskten de GERÇEKTEN
  kaybolduğu - 404 - doğrulandı). Toplam 69 test (13+3+53).
- **Not:** Aynı Content bounded context'indeki `BlogPost`/`MenuItem` hâlâ orphaned - bu turun kapsamı
  yalnızca anasayfa carousel'iydi.

## Sipariş Onay E-postası ve E-posta Şablonları

`IEmailSender` (Infrastructure'da gerçek bir SMTP çağrısı YERİNE yalnızca loglayan `SmtpEmailSender`
stub'ı ile), `Domain.Notifications.EmailTemplate` ve `NotificationLog` Faz 0/1'den beri hazırdı ama
Application katmanının HİÇBİR yerinden çağrılmıyordu - müşteri bir sipariş verdiğinde HİÇBİR zaman
bir onay e-postası (gerçek ya da simüle) tetiklenmiyordu.

- `PlaceOrderCommand` artık checkout başarıyla tamamlandıktan SONRA `IEmailSender.SendAsync`'i
  gerçekten çağırıyor - `"OrderConfirmation"`/`"tr"` anahtarlı bir `EmailTemplate` varsa onu
  (`{{OrderNumber}}`/`{{CustomerName}}`/`{{GrandTotal}}` yer tutucularını değiştirerek), yoksa
  dahili bir varsayılan metni kullanır - **checkout bu yüzden ASLA başarısız olmaz** (e-posta
  gönderimi tamamen izole bir try/catch içinde, sonucu bir `NotificationLog` satırına - başarılı ya
  da başarısız - kaydeder).
- Admin `/notifications/email-templates`: şablon oluşturma/düzenleme + son 20 gönderimin denetim
  geçmişi (kanal/alıcı/şablon/durum).
- 2 yeni regresyon testi (her checkout bir `NotificationLog` bırakır; şablonun aynı anahtar+dil ile
  ikinci kez oluşturulamaması) VE gerçek Storefront checkout'una karşı canlı HTTP ile doğrulandı -
  **özel bir şablon önceden kaydedilip gerçek bir sipariş verildi, sunucu loglarında yer
  tutucuların GERÇEKTEN doğru sipariş numarasıyla değiştirildiği görüldü** ("SMOKE-KONU:
  WEB-20260906060524-433 siparişiniz alındı"), Admin'in gönderim geçmişinde "Başarılı" olarak
  göründü. Toplam 71 test (13+3+55).
- **Önemli, açıkça belirtilen sınır:** E-posta gönderimi hâlâ GERÇEK bir SMTP sağlayıcısına bağlı
  DEĞİL (yalnızca sunucu loglarına yazılır) - ödeme sağlayıcılarıyla AYNI sınıf bir stub. Gerçek
  e-posta gönderimi için `Dekorras.Infrastructure/Notifications/NotificationSenders.cs`'teki
  `SmtpEmailSender`'ın gerçek bir SMTP/SendGrid/SES entegrasyonuyla değiştirilmesi gerekir.

## Blog

`Domain.Content.BlogPost` Faz 0/1'den beri vardı ama hiç Application/UI katmanı yoktu. CmsPage'in
çoklu dil desenini (bir `Translations` alt koleksiyonu + `SetTranslation` upsert) TEKRARLAMIYOR -
BlogPost'ta her dil çevirisi, AYNI slug ile `(Slug, LanguageCode)` benzersiz dizinine göre AYRI bir
satırdır (yeni bir dil = yeni bir kayıt, mevcut olanı güncellemez). Bu bilinçli bir tasarım farkı,
kod hatası değil.

- Admin `/cms/blog`: yazı oluşturma (slug + dil seçici, yalnızca oluştururken düzenlenebilir) /
  düzenleme (başlık+içerik) / yayınlama-yayından kaldırma, liste "Yayında"/"Taslak" rozetiyle.
- Storefront `/Blog` (liste) ve `/Blog/Details?slug=...` (detay): yalnızca YAYINDA olan yazılar
  görünür - taslak bir yazının detay adresine gidilirse 404 döner. Ana menüye "Blog" bağlantısı
  eklendi (`MainMenu` ViewComponent, "Anasayfa"nın hemen yanı).
- 2 yeni regresyon testi (taslak→yayında→yayından kaldırma geçişlerinde Storefront görünürlüğü;
  aynı slug'ın farklı dillerde ayrı kayıt olarak oluşturulabildiği ama aynı (slug, dil) ikilisinin
  tekrar kullanılamadığı) VE gerçek Admin/Storefront süreçlerine karşı canlı HTTP ile doğrulandı
  (Admin listesinde hem taslak hem yayındaki yazı göründü, Storefront'ta taslak 404 yayında olan 200
  + doğru başlık/HTML içerik döndü, anasayfa menüsünde "Blog" linki doğrulandı). Toplam 73 test
  (13+3+57).
- **Not:** Aynı Content bounded context'indeki `MenuItem` hâlâ orphaned - bu turun kapsamı yalnızca
  blog özelliğiydi.

## Rol/İzin (RBAC) Yönetimi ve Denetim Kaydı

`Domain.SystemAdmin.Role`/`Permission`/`RolePermission`/`AdminProfile` Faz 0/1'den beri vardı ve
`AppUserClaimsPrincipalFactory` tarafından giriş sırasında OKUNUYORDU (izin kontrolü zaten
çalışıyordu) ama roller/izinler/yönetici atamaları hiçbir yerden YAZILAMIYORDU - yalnızca
`DbInitializer`'ın tek seferlik SQL seed'i (`"Yönetici"` rolü + tek admin) vardı. **İkinci bir
yönetici kullanıcısı oluşturmanın HİÇBİR yolu yoktu.**

- Admin `/system/roles`: rol oluşturma, her rol için tüm izinlerin ver/al onay kutuları.
- Admin `/system/admins`: mevcut yönetici kullanıcıları (e-posta + ad + rol rozetleri) listeler,
  **yeni bir yönetici kullanıcısı GERÇEKTEN oluşturur** (`UserManager.CreateAsync` ile ASP.NET
  Identity kullanıcısı + `CreateAdminProfileCommand` ile domain profili, tek işlemde), rol ata/kaldır.
- Admin `/system/audit-log`: `AuditLog` (Faz 0/1'den beri vardı, yalnızca entegrasyon aktivasyonunda
  YAZILIYORDU, hiç OKUNMUYORDU) artık okunabiliyor; bu turda RBAC mutasyonlarının hepsi
  (rol oluşturma, izin ver/al, yönetici oluşturma, rol ata/kaldır) da denetim izine yazıyor.
- Domain'e simetri için `Role.Revoke`/`AdminProfile.RevokeRole` eklendi (yalnızca `Grant`/`AssignRole`
  vardı).
- **Bulunan gerçek hata (canlı smoke test sırasında yakalandı):** `CreateAdminProfileCommand`
  denetim kaydını YANLIŞ aktöre atfediyordu - `ActorIdentityUserId` alanına işlemi YAPAN yöneticinin
  değil, YENİ OLUŞTURULAN kullanıcının kendi ID'sini yazıyordu (iki parametre kolayca
  karıştırılmıştı). Düzeltme: komuta ayrı bir `ActingIdentityUserId` parametresi eklendi, Admin
  sayfası artık `AuthenticationStateProvider` üzerinden GERÇEK oturum sahibinin ID'sini okuyup
  geçiyor - regresyon testiyle de doğrulandı (denetim kaydının aktörünün işlemi yapan kullanıcı
  olduğu, konusu olan yeni kullanıcı DEĞİL).
- 2 yeni regresyon testi + gerçek Admin sürecine karşı canlı HTTP ile doğrulandı (ikinci bir
  yönetici oluşturulup rol atandı, `/system/roles`/`/system/admins`/`/system/audit-log` sayfalarının
  hepsinin doğru veriyi gösterdiği kanıtlandı). Toplam 75 test (13+3+59).
- **Bilinçli kapsam dışı:** `Setting` (key/value ayar deposu) hâlâ orphaned - hiçbir tanımlı
  kullanım senaryosu yok, sahte bir ayar ekranı UYDURMAK yerine gerçek bir ihtiyaç doğduğunda ele
  alınacak (Google Merchant alanlarıyla AYNI karar sınıfı).

## Çok Para Birimi (Görüntüleme)

Plan §7 (E-İhracat): "Ürün fiyatları temel para biriminde (TRY) tutulur; storefront ... güncel kur
üzerinden müşterinin seçtiği para biriminde **gösterim** yapar." `Domain.Localization.Currency`/
`ExchangeRate` Faz 0/1'den beri vardı, `IExchangeRateProvider` (sabit kurlu bir stub - gerçek TCMB/3.
parti API'si ile değiştirilmeye hazır) DI'a kayıtlıydı ama Application katmanının HİÇBİR yerinden
çağrılmıyordu.

- Storefront nav bar'a dil seçicinin yanına bir para birimi seçici eklendi (`dekorras_currency`
  çerezi, `StorefrontCurrency` - `StorefrontLanguage` ile AYNI desen).
- `CurrencyResultFilter` (global `IAsyncResultFilter`) her view render'ından ÖNCE seçili para
  biriminin TRY'ye göre güncel kurunu bir kez çözüp `ViewData`'ya yazar; `Html.Money(tryAmount)` /
  `Html.MoneyText(tryAmount)` (senkron, I/O YOK) bunu okuyup biçimlendirir. Bu ayrım BİLİNÇLİ: async
  kur çözümlemesi HER ZAMAN view render'ından önce olur, view'ların kendisi asla senkron-üzerinden-
  asenkron çağrı yapmaz (ileride gerçek bir HTTP tabanlı kur servisi eklendiğinde bile).
- Dönüştürülen sayfalar (alışveriş kararı verilen sayfalar): ürün listesi, ürün detayı, favoriler,
  sepet, checkout özeti.
- **Bilinçli olarak dönüştürülmeyen (kod yorumuyla da belirtildi):** `/Account/Orders` ve sipariş
  detayı HER ZAMAN TL gösterir - bunlar GERÇEKTEN tahsil edilen tutardır, bugünkü kurla "yaklaşık"
  bir değer göstermek yanıltıcı olurdu. Checkout sayfasında da TL dışı bir para birimi seçiliyken
  "gösterilen tutarlar yaklaşık dönüşümdür, ödemeniz TL olarak tahsil edilir" notu eklendi -
  **gerçek ödeme/sipariş tutarları bu turdan sonra da HER ZAMAN TRY'de hesaplanır ve tahsil edilir,
  yalnızca GÖSTERİM değişti.**
- 4 yeni regresyon testi (aktif para birimleri temel para birimi önce gelecek şekilde döner; temel
  para birimi için oran her zaman 1; yabancı para birimi için gerçek sağlayıcıdan doğru oran+sembol;
  tanımsız/pasif bir kod sessizce temel para birimine düşer) VE gerçek çalışan Storefront'a karşı
  canlı HTTP ile doğrulandı (gerçek bir ürün USD'ye geçilince 1.000 TRY'nin doğru şekilde 29.41 USD
  gösterdiği - hem ürün listesinde hem detayında hem sepette hem checkout'ta tutarlı - ve TL'ye geri
  dönünce doğru şekilde ₺ gösterime döndüğü kanıtlandı). Toplam 79 test (13+3+63).

## Hangfire ile Periyodik Kur Güncelleme

Plan §7'nin "güncel kur ... Hangfire ile periyodik güncellenir" cümlesinin devamı: `Hangfire.*`
NuGet paketleri Faz 0/1'den beri projeye kuruluydu ama HİÇBİR YERDE (`AddHangfire` bile) hiç
çağrılmıyordu - kurulu ama tamamen kablosuz bir alt sistem.

- Yalnızca `Dekorras.Api`'de SQL Server depolamalı bir Hangfire sunucusu çalışır - Admin/Storefront
  Hangfire'ı HİÇ bilmez, yalnızca sonucu (`ExchangeRates` tablosu) okur. Bilinçli karar: aynı işi üç
  ayrı süreçte zamanlamak yerine TEK bir "arka plan işleri" barındırıcısı (Api) seçildi.
- `RefreshExchangeRatesCommand` her AKTİF yabancı para birimi için `IExchangeRateProvider`'dan kuru
  çekip günde BİR `ExchangeRate` anlık görüntüsü yazar (aynı gün tekrar çalışırsa yinelenen satır
  oluşturmaz). `GetCurrencyConversionQuery` artık bu önbelleği CANLI sağlayıcı çağrısına TERCİH eder
  (önbellek yoksa - taze ortam - eskisi gibi canlı çağrıya düşer).
- **Bulunan gerçek çalışma zamanı hatası (canlı smoke testte, derleme YAKALAYAMADI):** İlk yazımda
  statik `RecurringJob.AddOrUpdate<T>(...)` API'si kullanılmıştı - Api başlatılınca
  `InvalidOperationException: Current JobStorage instance has not been initialized yet` ile çöktü.
  Sebep: `services.AddHangfire(...)` (DI tabanlı, YENİ API) `JobStorage.Current` statik alanını
  AYARLAMAZ - yalnızca eski `GlobalConfiguration.Configuration.UseSqlServerStorage(...)` çağrısı
  yapılsaydı statik API çalışırdı. Düzeltme: `IRecurringJobManager` (servis tabanlı, DI'dan çözümlenen
  API) kullanıldı - Hangfire'ın kendi hata mesajı bu düzeltmeyi doğrudan söylüyordu.
- Canlı doğrulama: Api başlatılıp Hangfire SQL şemasının GERÇEKTEN kurulduğu (log'da
  "Hangfire SQL objects installed") ve sunucunun başladığı görüldü; iş Hangfire'ın kendi
  `IRecurringJobManager`/`RecurringJob.TriggerJob` API'si ile ANINDA tetiklenip GERÇEKTEN çalışan
  Api sürecinin bunu işleyip 3 doğru `ExchangeRate` satırı (TRY→USD=0,0294, TRY→EUR=0,0270,
  TRY→GBP=0,0233 - `ExchangeRateProvider`'ın sabit tablosuyla birebir eşleşiyor) yazdığı SQL log'larda
  ve doğrudan sorguyla doğrulandı. **Bu 3 satır BİLİNÇLİ OLARAK dev DB'den silinmedi** - diğer
  smoke testlerin aksine bunlar sahte/işaretli test verisi DEĞİL, özelliğin GERÇEK ve istenen üretim
  durumu (iş zaten yarın tekrar çalışıp günlük bir anlık görüntü daha ekleyecek).
- 2 yeni regresyon testi (iş her aktif yabancı para birimi için bir anlık görüntü yazar, aynı gün
  tekrarlanmaz; dönüşüm sorgusu KASITLI OLARAK farklı bir önbellek değeriyle test edilip canlı
  sağlayıcı DEĞİL önbelleğin döndüğü kanıtlandı). Toplam 81 test (13+3+65).
- **Kapsam dışı:** Hangfire Dashboard (`/hangfire` UI) bilinçli olarak eklenmedi - varsayılan
  yetkilendirmesi yalnızca localhost'a izin verir, üretime uygun bir yetkilendirme filtresi ayrı bir
  güvenlik kararı gerektirir; bu turun kapsamı yalnızca periyodik kur güncellemesiydi.

## Domain.Shipping Temizliği ve Gümrük Beyanı

`ShippingMethod`/`ShippingRate`/`ShipmentTracking` Faz 0/1'den beri vardı ama `ICargoProvider`'ın
GetRateAsync/CreateShipmentAsync/GetTrackingStatusAsync'iyle İŞLEVSEL OLARAK ÇAKIŞIYORDU - muhtemelen
Provider Registry deseni benimsenmeden ÖNCEKİ bir tasarım yinelemesinden kalma redundant/ölü koddu
(gerçek kargo ücreti/takip mekanizması zaten `ICargoProvider` üzerinden çalışıyor, bu üçü hiçbir
yerden hiç kullanılmıyordu). Kullanıcıyla netleştirildikten sonra üçü de KODTAN TAMAMEN KALDIRILDI
(yeni bir `RemoveRedundantShippingModels` migration'ı ile tabloları da düşürüldü) - okuyan biri artık
iki rakip kargo modeli görmeyecek.

`CustomsDeclaration` ise GERÇEKTEN farklı bir kavramdı (plan §7 - "Ürünlerde HS/GTİP kodu alanı") ve
bu turda gerçek bir akışa bağlandı:

- `PlaceOrderCommand` artık Türkiye DIŞINA giden her sipariş için checkout anında otomatik bir gümrük
  beyanı TASLAĞI oluşturuyor - sipariş kalemlerinin `Product.HsCode`'larından bir özet (bir üründe hiç
  girilmemişse "Belirtilmemiş - Admin tarafından girilmeli" notu), beyan edilen değer (`SubTotalTry`),
  içerik açıklaması (ürün adları). Yurtiçi (TR) siparişlerde hiç oluşturulmaz.
- Admin sipariş detayında ("Gümrük Beyanı" kartı, yalnızca varsa görünür) `UpdateCustomsDeclarationCommand`
  ile düzeltilebilir - ör. bir üründe HS kodu hiç girilmemişse.
- **Bilinçli, açıkça belirtilen kapsam dışı:** proforma fatura üretimi ve yurt dışı satışlarda %0 KDV
  istisnası hesaplama mantığı - bunlar `CustomsDeclaration`'la ilişkili ama AYRI, daha büyük bir
  tasarım kararı gerektiriyor; bu turda yalnızca gümrük beyanı taslağı kapatıldı.
- 3 yeni regresyon testi (uluslararası sipariş gerçek HS koduyla doğru beyan oluşturur; yurtiçi
  sipariş için hiç oluşturulmaz; HS kodu girilmemiş bir ürün için "Belirtilmemiş" notu yazılıp Admin
  tarafından düzeltilebilir) VE gerçek çalışan Admin'e karşı canlı HTTP ile doğrulandı (uluslararası
  bir sipariş seed edilip sipariş detayında doğru HS kodu/değer/açıklamanın göründüğü, Admin
  düzeltmesinin GERÇEKTEN kalıcı olduğu kanıtlandı). Toplam 84 test (13+3+68).

## Ürün Karşılaştırma ve Listeden Hızlı Sepete Ekleme

Plan §2.1 ("ürün listeleme sayfası: ... ürün karşılaştırma listesi, liste üzerinden hızlı 'Sepete
Ekle'") açıkça bir MVP özelliğiydi ama hiç yazılmamıştı - bu, bir "orphaned entity" DEĞİLDİ (hiçbir
Domain modeli bile yoktu), tamamen eksik bir özellikti.

- Ürün listeleme/kategori/arama sayfalarındaki (`_ProductGrid.cshtml` - Home/Category/Search'ün
  hepsi tarafından paylaşılır) her kartta artık gerçek bir "Sepete Ekle" (varyantsız, hızlı - varyant
  seçimi gerekiyorsa müşteri ürün detayına gitmeli) ve "Karşılaştır" butonu var.
- Karşılaştırma listesi bir hesaba/DB kaydına DEĞİL, bir çerezde tutulur (`StorefrontCompareList` -
  `StorefrontCurrency`/`StorefrontLanguage` ile AYNI desen) - geçici bir gözatma yardımcısı olduğu
  için kalıcı bir niyet taşımaz, en fazla 4 ürün (en eskisi otomatik düşer).
- `/Compare` sayfası seçili ürünleri yan yana bir tabloda gösterir - fiyat/stok + `ProductAttribute`/
  `ProductAttributeValue`'dan (devamı 29) gelen TÜM özellik satırları (her ürünün FARKLI özellikleri
  olabilir, birleşik satır listesi tüm ürünlerin özellik adlarının BİRLEŞİMİDİR, eksik olan hücrede
  "—" gösterilir). Taslak/yayından kaldırılmış bir ürün listeden sessizce düşer.
- Nav bar'a seçili ürün sayısını gösteren bir "Karşılaştır (N)" linki eklendi.
- 3 yeni regresyon testi (farklı özelliklere sahip iki ürün doğru birleşik satırlarla karşılaştırılır;
  taslak ürün listeden sessizce düşer; boş liste boş sonuç döner) VE gerçek çalışan Storefront'a karşı
  canlı HTTP ile doğrulandı (iki gerçek ürün eklenip nav sayacının (1)→(2) arttığı, `/Compare`
  sayfasının doğru fiyat/özellik satırlarını gösterdiği, bir ürün listeden çıkarılınca DİĞERİNİN
  doğru şekilde kalmaya devam ettiği, hızlı "Sepete Ekle"nin gerçekten sepete eklediği kanıtlandı).
  Toplam 87 test (13+3+71).
- **Bu turda kendi test script'imden kaynaklanan (koda değil) bir yanlış alarm:** "ürünü listeden
  çıkarınca diğer ürün de kayboluyor" gibi göründü - gerçek sebep, çok adımlı akışı (ekle→ekle→çıkar)
  AYRI PowerShell çağrılarına bölmemdi (her biri kendi çerez kavanozuyla başlıyor, devamı 15'teki
  AYNI hata sınıfı). Tek bir sürekli script'te tekrarlanınca sorunsuz çalıştı.

## Ürün Listeleme: Sıralama ve Sayfalama

Plan §2.1'in aynı cümlesinin devamı: "sıralama (varsayılan, ad A-Z/Z-A, ucuzdan-pahalıya, pahalıdan-
ucuza, puana göre, ürün koduna göre), sayfa başına gösterim (12/25/50/75/100)" - Ürün Karşılaştırma
gibi bu da hiç yazılmamış bir MVP özelliğiydi (Kategori/Arama sayfaları `Page: 1, PageSize: 48`
sabit değerlerle çalışıyordu, hiç sıralama/sayfalama kontrolü yoktu).

- `GetStorefrontProductsQuery` artık bir `ProductSortOrder` (Varsayılan/Ad A-Z/Ad Z-A/Ucuzdan
  Pahalıya/Pahalıdan Ucuza/Puana Göre/Ürün Koduna Göre) alıyor ve `PagedResult<T>` (Items +
  TotalCount + Page + PageSize + TotalPages) döndürüyor - önceki düz koleksiyon dönüşü BREAKING
  CHANGE oldu, HomeController/CategoryController/SearchController'ın hepsi güncellendi.
- "Puana göre" sıralama onaylanmış (`ProductReview.IsApproved`) yorumların ortalamasını KORELASYONLU
  BİR ALT SORGU olarak SQL'e çevirir - bu, EF Core LINQ çevirisinin en riskli noktasıydı, gerçek SQL
  Server'a karşı ayrı bir regresyon testiyle doğrulandı.
- Kategori/Arama sayfalarına gerçek bir Sırala + Sayfa Başına açılır kutusu (`_ProductFilters.cshtml`)
  ve sayfa numarası/Önceki-Sonraki linkleri (`_Pagination.cshtml`) eklendi - sayfa boyutu yalnızca
  plan'ın belirttiği 5 değerle (12/25/50/75/100) sınırlı, geçersiz bir değer sessizce 25'e düşer.
- 4 yeni regresyon testi (fiyata göre artan/azalan; ada göre ve ürün koduna göre; **puana göre
  sıralamanın onaylanmış yorum ortalamasını kullandığı**; sayfalamanın toplam sayımı koruyup doğru
  dilimi döndürdüğü) VE gerçek çalışan Storefront'a karşı canlı HTTP ile doğrulandı (gerçek 3 ürün
  seed edilip fiyata göre azalan ve ada göre azalan sıralamaların doğru sırayı ürettiği kanıtlandı).
  Toplam 91 test (13+3+75).

## İletişim Kanalları ve "Hemen Al"

Plan §2.1/§2.2'nin devamı: "İletişim kanalları: sabit telefon hattı, WhatsApp Business linki,
Instagram, Telegram — footer/header üzerinden doğrudan erişim" ve "Sepete Ekle" / "Hemen Al".

- `Domain.SystemAdmin.Setting` (Faz 0/1'den beri vardı, hiçbir tanımlı kullanım senaryosu olmadığı
  için bilinçli olarak orphaned bırakılmıştı - bkz. devamı 22/43/45) bu turda İLK gerçek amacına
  kavuştu: Admin `/settings/contact` (Telefon/WhatsApp/Instagram/Telegram, `UpsertSettingCommand` ile
  upsert), Storefront footer'ı yalnızca DOLU olan kanalları gösterir (boş bırakılan bir alan HİÇ
  render edilmez, boş bir link olarak değil).
- Ürün detay sayfasına "Hemen Al" butonu eklendi (`Cart/BuyNow`) - ürünü sepete ekleyip müşteriyi
  sepet sayfasını hiç göstermeden DOĞRUDAN checkout'a yönlendirir; "Sepete Ekle" ile AYNI formu
  paylaşır (`formaction` ile farklı bir uç noktaya gönderir).
- 2 yeni regresyon testi (bir ayar ikinci kez ayarlanınca günceller, yinelenen satır oluşturmaz;
  hiç ayarlanmamış bir anahtar sonuçta hiç görünmez) VE gerçek çalışan Admin+Storefront'a karşı
  canlı HTTP ile doğrulandı (Telefon/WhatsApp/Instagram ayarlanıp Telegram BİLİNÇLİ OLARAK boş
  bırakıldı - Storefront footer'ında ilk üçünün göründüğü, Telegram'ın hiç görünmediği; "Hemen Al"
  butonuna basılınca gerçek ürünün doğru tutarla checkout özetinde göründüğü kanıtlandı). Toplam
  93 test (13+3+77).

## E-Ticaret Modülü Dashboard Metrikleri

Plan §2.3'ün Admin dashboard'ı - "Toplam Sipariş"/"Toplam Satış"/"Toplam Kategori"/"Toplam Müşteri"/
"Toplam Ürün" kartları `/ecommerce` ana sayfasına eklendi, ayrıca "Son Siparişler" tablosu (mevcut
`GetOrdersQuery`'nin yeniden kullanımı).

- "Toplam Satış" iptal/red/başarısız/hükümsüz/ters ibraz/süresi dolmuş/iade edilmiş siparişleri
  BİLİNÇLİ OLARAK HARİÇ TUTAR - bunlar gerçekleşmemiş ya da geri alınmış geliri temsil eder (kod
  yorumuyla açıkça belgelendi, tek doğru cevabı olmayan bir muhasebe kararı).
- **Bilinçli olarak kapsam dışı bırakılan üç metrik** (plan'da geçiyor ama gerçek altyapı gerektirir):
  "Çevrimiçi Ziyaretçi" (gerçek zamanlı oturum/presence takibi - SignalR/Redis, sahte bir sayı
  UYDURULMADI), Türkiye haritası bölgesel dağılımı (bir geo-görselleştirme kütüphanesi gerektirir),
  sipariş/müşteri trend grafiği (bir grafik kütüphanesi gerektirir) - üçü de AYRI bir tasarım kararı/
  iş yatırımı gerektiriyor.
- 1 yeni regresyon testi (metrik sayımlarının doğru olduğu VE toplam satışın geçersiz siparişleri
  gerçekten hariç tuttuğu) VE gerçek çalışan Admin'e karşı canlı HTTP ile doğrulandı.
- **Bu turda GERÇEK bir hata bulunup düzeltildi (kendi test kodumda, ürün kodunda DEĞİL):** Yeni
  regresyon testi mevcut `AccountingDashboardRegressionTests`'in KULLANDIĞI AYNI test veritabanı
  adını ("DekorrasDashboardTests") seçmişti - xunit test sınıflarını PARALEL çalıştırdığı için, iki
  sınıf aynı DB'yi eşzamanlı `EnsureCreated`/`EnsureDeleted` ettiğinde ARA SIRA "Cannot open database"
  hatasıyla YALNIZCA tam test paketi çalıştırıldığında (izole çalıştırıldığında DEĞİL) başarısız
  oluyordu - klasik bir "paralel test izolasyonu" hatası. Düzeltme: benzersiz bir DB adına
  (`DekorrasECommerceDashboardTests`) geçildi, ardışık 4 tam paket çalıştırmasıyla doğrulandı.
  **Ders: yeni bir regresyon test dosyası yazarken, bağlantı dizesindeki DB adının PROJE GENELİNDE
  benzersiz olduğunu (`grep`) önceden kontrol et - isim çakışması yalnızca TAM paket çalıştırmasında,
  ARA SIRA ortaya çıkan, izole çalıştırmada asla YAKALANAMAYAN bir hata sınıfı.** Toplam 94 test
  (13+3+78).

## Stok Dışı Durumu ve Stoktan Düş

Plan §2.4'ün ürün veri modeli - "Stoktan Düş (E/H)" ve "Stok Dışı Durumu (2-3 gün içinde / Ön
Sipariş / Stokta var / Stokta yok)". `Product.TrackStock`/`StockAvailability` Faz 0/1'den beri TÜM
bu durumları modelliyordu ama `UpdateStock` yalnızca InStock/OutOfStock'u otomatik türetiyordu -
`PreOrder`/`ArrivesInDays` hiçbir yerden seçilemiyordu VE `TrackStock` hiçbir yerden değiştirilemiyordu
(her zaman varsayılan `true`) - bu ikisi BİRLİKTE kapatılması gereken, birbirine bağımlı iki orphaned
alandı (yalnızca `StockAvailability`'yi kapatmak `TrackStock` hep `true` kaldığı sürece anlamsız
kalırdı, çünkü her `UpdateStock` çağrısı elle seçilen durumu geri ezerdi).

- Admin ürün düzenlemede "Stoktan Düş" onay kutusu + "Stok Dışı Durumu" açılır kutusu (4 seçenek).
- Storefront ürün detayında artık 4 farklı rozet (Stokta Var/Yok/Ön Sipariş/2-3 Gün İçinde) - yalnızca
  GERÇEKTEN tükenmiş (`OutOfStock`) bir ürün "Sepete Ekle"/"Hemen Al"yı gizler, Ön Sipariş/2-3 gün
  içinde durumları hâlâ sipariş verilebilir kabul edilir.
- **Bilinçli davranış (kod yorumuyla belgelendi):** "Stoktan Düş" AKTİFSE, checkout'taki `UpdateStock`
  bu alanı GERÇEK stok miktarına göre Stokta Var/Yok'a GERİ DÖNDÜRÜR - "Ön Sipariş"/"2-3 Gün İçinde"
  yalnızca "Stoktan Düş" KAPALI ürünlerde (sipariş üzerine tedarik edilenler) kalıcıdır.
- 2 yeni regresyon testi (stok takibi kapalıyken Ön Sipariş durumu GERÇEKTEN kalıcı kalır - stok
  miktarı değişse bile; stok takibi açıkken elle seçilen durum bir sonraki `UpdateStock`'ta otomatik
  ezilir) VE gerçek çalışan Admin+Storefront'a karşı canlı HTTP ile doğrulandı. Toplam 96 test
  (13+3+80).

## İlgili Ürünler

Plan §2.4'ün "Bağlantılar" sekmesi - "ilgili ürünler". Faz 0/1'de hiç modellenmemişti (orphaned bir
entity DEĞİL, tamamen eksik bir özellikti) - yeni bir `RelatedProduct` join tablosu ve migration
eklendi.

- Admin ürün düzenlemede "İlgili Ürünler" bölümü - ürün kodu yazılarak eklenir, tabloda durumuyla
  (Aktif/Taslak) birlikte listelenir, kaldırılabilir.
- İlişki BİLİNÇLİ OLARAK TEK YÖNLÜDÜR - A'ya B eklenmesi B'nin listesine A'yı OTOMATİK eklemez (admin
  her ürünün "Benzer Ürünler" bölümünü kendi bağlamında ayrı ayrı küratörlüğünü yapar).
- Storefront ürün detay sayfasına "Benzer Ürünler" bölümü eklendi (mevcut `_ProductGrid` partial'ı
  yeniden kullanılır) - yalnızca Aktif ilgili ürünler görünür, taslak/yayından kaldırılmış bir ilgili
  ürün sessizce düşer.
- 3 yeni regresyon testi (ekleme/ikinci kez eklememe/kaldırma + tek yönlülük; bir ürün kendisiyle
  ilişkilendirilemez; taslak bir ilgili ürün Storefront'ta görünmez ama Admin görür) VE gerçek
  çalışan Admin+Storefront'a karşı canlı HTTP ile doğrulandı. Toplam 99 test (13+3+83).

## Sipariş Listesi Filtreleme

Plan §2.5 - "sipariş listesinde ... filtreleme (sipariş no, müşteri, durum, tutar, tarih aralığı)".
`GetOrdersQuery` Faz 0/1'den beri yalnızca "durum" filtresini destekliyordu.

- Admin `/ecommerce/orders`'a Sipariş No (kısmi eşleşme), Müşteri (kısmi eşleşme), Min/Max Tutar ve
  Başlangıç/Bitiş Tarihi filtreleri eklendi - hepsi BİRLİKTE (AND mantığıyla) çalışır.
- Bitiş tarihi seçilen günün SONUNA (23:59:59) kadar genişletilir - aksi halde o günün siparişleri
  "gece yarısından önce" olduğu için aralığın dışında kalırdı (kod yorumuyla belgelendi).
- 5 yeni regresyon testi (sipariş no/müşteri kısmi eşleşme; tutar aralığı; tarih aralığının gün
  sonunu kapsadığı VE gelecekteki bir aralığın boş döndüğü; birleştirilmiş filtrelerin AND mantığıyla
  çalıştığı) VE gerçek çalışan Admin'e karşı canlı HTTP ile doğrulandı. Toplam 104 test (13+3+88).

## Müşteri Grubu Fiyatlandırması (B2B/B2C)

Plan §2.6 - "müşteri grupları: ... ürün fiyatlarını sadece belirli gruplara gösterme opsiyonu
mevcut". `Product.GroupPrices`/`SetGroupPrice` (Domain) Faz 0/1'den beri vardı ama hiçbir yerden
çağrılmıyordu - sepet/checkout/vitrin fiyat hesaplamasının HİÇBİRİ bunu dikkate almıyordu, yani
gerçek bir B2B/kurumsal müşteri her zaman SESSİZCE perakende fiyattan tahsil edilirdi. Bu turda
üç tüketim noktasının da (sepet, sipariş, vitrin) AYNI önceliği uygulaması sağlandı.

- **`ProductPricingHelper.ResolveUnitPriceTry`** - tek durak fiyat çözümleme noktası. Öncelik:
  (1) toplu alım kademesi (`QuantityDiscount`, miktar eşiğini geçen en yüksek kademe - kimden
  bağımsız) > (2) müşteri grubuna özel fiyat (`ProductGroupPrice`) > (3) taban fiyat. Kademe fiyatı
  varsa grup fiyatından ucuz olsa bile HER ZAMAN kazanır (bilinçli tasarım - toplu alım indirimi
  herkese eşit uygulanan bir kampanya, grup fiyatı ise kişiye özel bir liste fiyatı).
- `Product.GetPriceFor` yerine BİLİNÇLİ OLARAK doğrudan repository sorgusu kullanılır - aksi halde
  `GetByIdAsync` ile pasif gezinme yoluyla materyalize edilmiş bir üründe `GroupPrices` koleksiyonu
  önceden `LoadCollectionAsync` ile yüklenmediği sürece sessizce boş görünürdü (bkz. yukarıdaki
  "Önemli mimari not").
- `AddCartItemCommand`/`UpdateCartItemQuantityCommand`/`PlaceOrderCommand`'ın üçü de artık
  `CustomerGroupId` alıyor ve `ProductPricingHelper` çağırıyor. Checkout'ta fiyat, sepete
  eklendiğinden beri değişmiş olabileceği için ANINDA yeniden hesaplanır (zaten var olan davranış).
- `GetStorefrontProductsQuery`/`GetProductBySlugQuery` isteğe bağlı bir `CustomerGroupId` parametresi
  aldı; Storefront controller'ları (`Home`/`Category`/`Search`/`Product`/`Cart`) giriş yapmış
  müşterinin grubunu `GetMyCustomerGroupIdQuery` ile her istekte taze çözümleyip iletiyor
  (`StorefrontCustomerGroup.ResolveAsync` yardımcı sınıfı). Misafirler için `null` döner, çağıran
  taraf taban fiyata düşer.
- Admin ürün düzenlemede yeni "Müşteri Grubu Fiyatları" tablosu - her müşteri grubu için fiyat
  girilip kaydedilebilir/boş bırakılıp kaldırılabilir (`SetProductGroupPriceCommand`/
  `RemoveProductGroupPriceCommand`).
- **Bilinçli kapsam dışı bırakmalar:** (1) Ürün listesindeki fiyat ARALIĞI filtresi/sıralaması
  BİLİNÇLİ OLARAK hâlâ taban fiyata göre çalışır - nadir kullanılan bir B2B kişiselleştirmesi için
  liste-genelinde bir sıralama/filtre karmaşıklığına girmeye değmez. (2) `GetActiveRelatedProductsQuery`/
  `GetProductsForComparisonQuery` (ilgili ürünler/karşılaştırma) grup fiyatı KULLANMAZ - ikincil
  görünümler, düşük öncelik. (3) `Cart.CustomerId` (misafir sepetinin bir kimlikle asla
  ilişkilendirilmemesi) bu turda keşfedilen ayrı bir orphaned alan - daha büyük bir UI/UX kararı
  gerektirdiği için BİLİNÇLİ OLARAK bu kapsamın dışında bırakıldı.
- 4 yeni regresyon testi (grup fiyatı ayarlama/güncelleme/kaldırma; kademe>grup>taban önceliği;
  sepete eklemede misafir/üye/farklı-grup ayrımı; vitrin listesi+detayının grup fiyatını doğru
  göstermesi) VE gerçek çalışan Admin+Storefront'a karşı canlı HTTP ile doğrulandı (misafir olarak
  taban fiyat 500,00 ₺; "Kurumsal" grubuna taşınan gerçek bir müşteri hesabıyla giriş yapıldığında
  350,00 ₺; Admin ürün düzenleme sayfasında Kurumsal satırının 350 ile önceden dolu geldiği). Toplam
  108 test (13+3+92).

## Müşteri Grubuna Göre Fiyat Gizleme

Yukarıdaki Müşteri Grubu Fiyatlandırması bölümünde BİLİNÇLİ OLARAK ertelenen `CustomerGroup.
ShowPricesOnStorefront` bayrağı bu turda kapatıldı (Faz 0/1'den beri domain'de vardı, hiçbir yerden
set edilemiyordu - her zaman varsayılan `true` kalıyordu).

- Admin'de yeni `/ecommerce/customer-groups` sayfası - her müşteri grubu için "Fiyatı Gizle"/
  "Fiyatı Göster" geçişi (`SetCustomerGroupShowPricesCommand`).
- `GetStorefrontProductsQuery`/`GetProductBySlugQuery`'nin DTO'larına `PricesVisible` bool'u eklendi.
  Hesaplama tek bir sorguyla yapılır (istek başına TEK bir müşteri grubu söz konusu olduğu için
  satır başına değil, sayfa başına bir kez): `CustomerGroupId` `null` ise (misafir) HER ZAMAN
  `true`; aksi halde ilgili grubun `ShowPricesOnStorefront` değeri kullanılır.
- Storefront'ta (`_ProductGrid` partial + ürün detay sayfası) `PricesVisible=false` olduğunda fiyat
  yerine "Fiyat için bizimle iletişime geçin" mesajı gösterilir VE "Sepete Ekle"/"Hemen Al" butonları
  gizlenir - fiyatı göremeyen bir müşterinin sepete ekleyip fiyatı birkaç tık sonra sepette görmesi
  özelliğin amacını boşa çıkarırdı, bu yüzden satın alma yolu da aynı anda kapatıldı.
- `GetActiveRelatedProductsQuery` (Benzer Ürünler) BİLİNÇLİ OLARAK bu bayrağı da uygulamaz - aynı
  gerekçeyle (ikincil görünüm) grup fiyatını da uygulamıyordu.
- **Canlı HTTP ile karşılaşılan (kod hatası DEĞİL, kendi smoke script'ime ait) bir tuzak burada da
  tekrarlandı:** `ProductController.Details(string slug)`'ın `slug` parametresi varsayılan
  `{controller}/{action}/{id?}` route'undaki HİÇBİR yer tutucuyla eşleşmiyor - doğru URL biçimi
  path segment DEĞİL, sorgu dizesidir: `/Product/Details?slug=...` (bkz. Razor'daki `asp-route-slug`
  tag helper'ının ürettiği gerçek URL). Yanlış biçim sessizce `WHERE 0=1`'e optimize olup 404 döner.
- 2 yeni regresyon testi (bayrağın açılıp kapanabildiği; misafirin HER ZAMAN gördüğü, fiyatı açık
  bir grubun üyesinin gördüğü, fiyatı kapalı bir grubun üyesinin GÖRMEDİĞİ - hem liste hem detay
  sorgusunda) VE gerçek çalışan Admin+Storefront'a karşı canlı HTTP ile doğrulandı (misafir 999,00 ₺
  görüyor; "Smoke Bayi Onay Bekliyor" grubuna taşınan bir müşteri hem anasayfa listesinde hem ürün
  detayında fiyat yerine "bizimle iletişime geçin" mesajını görüyor VE Sepete Ekle butonu hiç
  render edilmiyor; Admin `/ecommerce/customer-groups` sayfası üç grubu da doğru rozetle - ikisi
  "Gösteriliyor", biri "Gizli" - listeliyor). Toplam 110 test (13+3+94).

## Sepetin Cihazlar Arası Taşınması (Cart.CustomerId)

Bu turda son BİLİNÇLİ ertelenen orphaned alan da kapatıldı: `Cart.CustomerId` Faz 0/1'den beri
vardı ama hiçbir yerden set edilmiyordu - bir müşterinin sepeti YALNIZCA o anki tarayıcı çerezine
(`CartSession`, 30 gün kalıcı) bağlıydı, başka bir cihazdan/tarayıcıdan asla erişilemezdi.

- Yeni `MergeGuestCartIntoCustomerCommand` giriş/kayıt anında (`AccountController.Login`/`Register`)
  çağrılır. Üç senaryoyu ele alır: (1) müşterinin başka bir cihazda kayıtlı sepeti yoksa, bu
  oturumun (varsa) misafir sepetini müşteriye bağlar; (2) müşterinin kayıtlı sepeti var ama bu
  cihazda hiç sepet yoksa, kayıtlı sepeti bu oturumun anahtarına TAŞIR (`Cart.SetSessionKey`) ki
  gelecekteki istekler bulabilsin; (3) HER İKİSİ de varsa birleştirir - çakışmayan ürünler
  eklenir, aynı üründen ikisinde de varsa AKTİF (bu oturumun) satır korunur, eski sepet satırı
  silinir (yinelenen sepet oluşmaz).
- `AddCartItemCommand`'a da `CustomerId` eklendi - giriş yapmış bir müşterinin İLK "Sepete Ekle"
  tıklamasında (henüz hiç sepeti yokken) oluşturulan sepet doğrudan doğru müşteriye bağlanır.
- **Bilinçli güvenlik kararı:** bu cihazın (session key'in) sepeti ZATEN başka, bilinen bir
  müşteriye aitse (o müşteri çıkış yapmadan tarayıcıyı kapatmış olabilir), giriş yapan YENİ müşteri
  o sepete DOKUNMAZ/devralmaz - aksi halde bir müşteri paylaşılan bir cihazda başka birinin sepetini
  "çalabilir" ve o sepetle ödeme yapabilirdi. Bu koruma eklendiği için `AccountController.Logout`'a
  da `CartSession.ClearSessionKey` eklendi (çerez temizlenir) - normal akışta bu senaryoya hiç
  düşülmez, üstteki koruma yalnızca çıkış yapılmadan tarayıcı kapatılan istisnai durum için bir
  güvenlik ağıdır.
- 4 yeni regresyon testi (ilk sepetin misafir sepetine bağlanması; başka cihazdaki sepetin bu
  oturuma taşınması - yinelenen sepet oluşmadığı doğrulanarak; iki cihazdaki sepetlerin çakışmayan
  ürünlerde birleşip çakışanda aktif satırın korunduğu; başka bir müşteriye ait bir sepete
  dokunulmadığı) VE gerçek çalışan Storefront'a karşı canlı HTTP ile İKİ AYRI `WebRequestSession`
  (iki farklı cihaz simülasyonu) kullanılarak doğrulandı: cihaz 1'de misafirken ürün A sepete
  eklenip kayıt olundu (sepet korundu), cihaz 2'de (tamamen ayrı bir çerez) misafirken ürün B
  eklenip AYNI hesaba giriş yapıldı - giriş sonrası cihaz 2'nin sepeti hem A hem B'yi gösterdi ve
  DB'de müşteri için TEK bir sepet satırı kaldığı (cihaz 1'in eski satırı silindi, yinelenmedi)
  doğrulandı. Toplam 114 test (13+3+98).

## Güvenlik Sertleştirmesi: Rate Limiting ve Güvenlik Başlıkları

Plan §11 - "reCAPTCHA v3, rate limiting, güvenlik başlıkları (CSP, HSTS)". reCAPTCHA gerçek API
kimlik bilgisi gerektirdiği için (bkz. devamı 24'teki aynı gerekçe - ödeme sağlayıcıları) BİLİNÇLİ
OLARAK yazılmadı; HSTS zaten Faz 1'den beri her üç uygulamada da aktifti. Rate limiting ve temel
güvenlik başlıkları ise üçüncü taraf kimlik bilgisi GEREKTİRMEDİĞİ için bu turda eklendi.

- **Rate limiting** (.NET'in yerleşik `Microsoft.AspNetCore.RateLimiting` middleware'i, ek paket
  gerekmez): IP başına sabit pencereli sınırlayıcı (`FixedWindowLimiter`, dakikada 5 istek,
  aşımda kuyruğa almadan doğrudan `429 Too Many Requests`) - Storefront'un `/Account/Login` ve
  `/Account/Register`, Admin'in `/Account/Login` (minimal API uç noktası), Api'nin
  `/api/v1/auth/register`/`/login`/`/refresh` uç noktalarına uygulandı. Kimlik bilgisi kabul eden
  ve brute-force/credential-stuffing'e açık uç noktalarla sınırlı tutuldu - genel bir global
  sınırlayıcı BİLİNÇLİ OLARAK eklenmedi (ör. Admin'in Blazor Server SignalR trafiğini veya normal
  sayfa gezinmesini yanlışlıkla sınırlama riski, hedeflenen kazanıma göre orantısız).
- **Güvenlik başlıkları:** `Dekorras.Infrastructure/Web/SecurityHeadersExtensions.cs` - her üç
  uygulamanın da paylaştığı `UseDekorrasSecurityHeaders()` ile `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin` eklendi. **CSP
  (Content-Security-Policy) BİLİNÇLİ OLARAK eklenmedi** - Admin'in Blazor Server SignalR devresi
  (WebSocket) ve envanterinin tam çıkarılmadığı olası inline script/stil kullanımları için doğru
  bir CSP, kapsamlı manuel test olmadan yanlış yapılandırılırsa TÜM etkileşimli butonları sessizce
  kırabilirdi (bu oturumda haftalarca inşa edilen Blazor işlevselliğini riske atacak, orantısız bir
  risk/değer dengesi).
- Otomatik regresyon testi eklenmedi (bu, HTTP pipeline/middleware davranışıdır - mevcut entegrasyon
  testleri handler'ları doğrudan çağırır, middleware'i hiç devreye sokmaz) - bunun yerine üç
  uygulamanın hepsine karşı canlı HTTP ile doğrulandı: her üçünde de 3 güvenlik başlığının varlığı
  VE art arda 7 hatalı giriş denemesinin ilk 5'inin normal işlenip (200/401 - gerçek iş mantığı
  hatası), 6. ve 7.'sinin `429` ile reddedildiği kanıtlandı.

## Admin Girişinde İki Faktörlü Kimlik Doğrulama (2FA)

Plan §11 - "admin panelde Blazor Server'ın oturum tabanlı kimlik doğrulaması + 2FA (mevcut sistemde
olmayan, önerilen bir iyileştirme)". ASP.NET Core Identity'nin yerleşik TOTP (Authenticator uygulaması
tabanlı) desteği kullanıldı - `AddDefaultTokenProviders()` zaten Faz 1'den beri kayıtlıydı, ek paket
gerekmedi.

- Yeni `/system/two-factor` sayfası - her admin KENDİ hesabı için 2FA'yı etkinleştirip
  kapatabilir. Etkinleştirme: bir paylaşılan anahtar (+ `otpauth://` URI) üretilir, admin bunu
  kimlik doğrulama uygulamasına (Google Authenticator/Microsoft Authenticator/Authy) ELLE girer
  (QR kod görseli BİLİNÇLİ OLARAK oluşturulmadı - yeni bir bağımlılık gerektirir, manuel anahtar
  girişi tüm authenticator uygulamalarınca zaten desteklenir), sonra uygulamadaki 6 haneli kodu
  girip doğrular.
- Admin'in giriş uç noktası (`/Account/Login`) `PasswordSignInAsync`'in `RequiresTwoFactor`
  sonucunu artık kontrol ediyor - 2FA etkin bir hesapta şifre doğru olsa bile oturum HENÜZ
  açılmaz, `/login-2fa` sayfasına yönlendirilir; yeni `/Account/LoginWith2fa` uç noktası
  `SignInManager.TwoFactorAuthenticatorSignInAsync` ile kodu doğrulayıp oturumu asıl o zaman açar.
  Bu iki uç nokta da mevcut `"auth"` rate limiting politikasını paylaşır.
- **Kapsam bilinçli olarak Admin ile sınırlı tutuldu** (plan metni de 2FA'yı özellikle "admin
  panelde" diye belirtiyor) - Storefront müşteri girişine eklenmedi.
- **Önemli bir teknik keşif (bu turda TESPİT EDİLDİ, canlı smoke test yazarken bulundu):**
  ASP.NET Core Identity'nin `AuthenticatorTokenProvider.GenerateAsync`'i BİLİNÇLİ OLARAK boş string
  döner - gerçek dünyada TOTP kodunu SUNUCU değil kullanıcının telefonundaki uygulama üretir,
  sunucu yalnızca `ValidateAsync`/`VerifyTwoFactorTokenAsync` ile doğrular. Bu, ürün kodunda bir
  hata DEĞİL (sayfa zaten doğru şekilde yalnızca `VerifyTwoFactorTokenAsync`'i kullanıyor) - ama
  smoke testin kendisinin "sunucudan bir kod iste" şeklindeki ilk yaklaşımı bu yüzden başarısız
  oldu; test RFC 6238'i (HOTP + 30 saniyelik zaman sayacı, HMAC-SHA1) kendisi hesaplayacak şekilde
  düzeltildi - gerçek bir authenticator uygulamasının yaptığının BİREBİR aynısı.
- Otomatik regresyon testi eklenmedi (ASP.NET Identity'nin kendi TOTP/cookie mekanizması test
  edilmiş durumda, bu bir entegrasyon katmanı özelliği) - bunun yerine çalışan Admin'e karşı UÇTAN
  UCA canlı HTTP ile doğrulandı: 2FA'sız normal giriş çalışıyor → kurulum sayfası anahtar üretiyor →
  (Blazor interaktif "Doğrula ve Etkinleştir" butonu düz HTTP ile sürülemediği için - bkz. devamı
  14/15'teki aynı sınırlama - gerçek UserManager çağrılarıyla simüle edilerek) 2FA etkinleştirildi →
  şifreyle giriş artık `/login-2fa`'ya yönleniyor → hesaplanan DOĞRU kod kabul edilip oturum açıyor
  → YANLIŞ kod reddedilip oturum AÇILMIYOR (korumalı sayfa denemesi `/login`'e geri yönleniyor) →
  2FA kapatılınca giriş tekrar doğrudan çalışıyor. Toplam 114 test (13+3+98, middleware/Identity
  davranışı olduğu için sayı değişmedi).

## Müşteri Grubu Değiştirme (Admin) + Ölü Kod Temizliği

Bu turda Domain katmanının TAMAMI için ("orphaned entity" taramasının bu oturumdaki SON turu)
yeniden bir tarama yapıldı ve iki bulgu kapatıldı:

- **`Customer.ChangeGroup(Guid)` Faz 0/1'den beri vardı ama hiçbir yerden çağrılamıyordu** - bir
  müşteri kaydolduktan sonra SONSUZA KADAR "Bireysel" grubunda kalıyordu, admin onu "Kurumsal"
  (B2B) grubuna hiç TAŞIYAMIYORDU. Bu, devamı 53'teki Müşteri Grubu Fiyatlandırması özelliğini
  gerçek hiçbir müşteriye UYGULANAMAZ kılan, gözden kaçmış bir eksikti (o turda yalnızca fiyat
  hesaplama/görüntüleme mantığı kablolanmış, "bir müşteriyi hangi gruba koyacağız" sorusu hiç
  sorulmamıştı). Yeni `SetCustomerGroupCommand` + Admin müşteri detay sayfasına grup seçim
  dropdown'ı eklendi - geçersiz bir gruba taşıma denemesi reddedilir.
- **`Product.SetDimensions(...)` ölü kod olarak SİLİNDİ** (yeni bir özellik eklenerek değil, kod
  KALDIRILARAK kapatıldı) - `SetPackageDimensions`+`SetWeight` (devamı ~ döneminde eklenen, ayrı ayrı
  düzenlenebilir versiyon) tarafından tamamen SÜPÜRÜLMÜŞ, hiçbir yerden çağrılmayan eski/yinelenen
  bir metottu.
- 1 yeni regresyon testi (grup değiştirme + geçersiz gruba taşımanın reddedilmesi) VE gerçek çalışan
  Admin'e karşı canlı HTTP ile (müşteri detay sayfasının dropdown'ının doğru grubu ÖNCEDEN seçili
  gösterdiği, Blazor interaktif dropdown'ın seçimi simüle eden gerçek komut çağrısından sonra
  sayfanın YENİ grubu gösterdiği) doğrulandı. Toplam 115 test (13+3+99).

**Bu son taramada bulunan, BİLİNÇLİ OLARAK bu turda ELE ALINMAYAN diğer orphaned öğeler** (gelecekte
ayrı birer karar/konuşma gerektiriyor, tahmin yürütüp yanlış varsayımla kod yazmak yerine burada
açıkça not ediliyor):
- `Order.SetMarketplaceReference` ve TÜM `Domain.Marketplace` bounded context'i
  (`MarketplaceAccount`/`MarketplaceListing`/`MarketplaceOrder`/vb.) - plan §9'un pazaryeri
  senkron connector'ları (Trendyol/Hepsiburada/N11/İdefix/Amazon) hiç yazılmadı; plan'ın KENDİSİ
  bunu "hangisinin aktifleştirileceği firmaya bırakılır" diyerek opsiyonel bir faz olarak
  işaretliyor VE gerçek API kimlik bilgisi olmadan anlamlı bir senkron mantığı yazılamaz (ödeme/
  kargo sağlayıcılarındaki AYNI gerekçe).

## Adres Defteri (Kayıtlı Adreslerim)

Devamı 58'de BİLİNÇLİ OLARAK ertelenen `Address.SetAsDefaultShipping`/`SetAsDefaultBilling` bu
turda kapatıldı - Storefront'a yeni `/Account/Addresses` ("Adreslerim") ekranı eklendi.

- Müşteri kendi adreslerini görüntüleyip yeni adres ekleyebilir, silebilir, birini "Varsayılan
  Teslimat"/"Varsayılan Fatura" olarak işaretleyebilir - aynı anda yalnızca BİR adres varsayılan
  olabilir (yeni bir tanesi işaretlenince eskisinin bayrağı otomatik kalkar).
- Yeni `AddCustomerAddressCommand`/`RemoveCustomerAddressCommand`/`SetDefaultShippingAddressCommand`/
  `SetDefaultBillingAddressCommand` - hepsi `IdentityUserId` üzerinden SAHİPLİK KONTROLÜ yapar
  (bkz. devamı 12'deki `OrderDetail` sahiplik kontrolüyle AYNI gerekçe) - aksi halde bir müşteri
  başka birinin adres GUID'ini tahmin ederek onu silebilir/değiştirebilirdi.
- **Bilinçli kapsam dışı bırakıldı (devamı 58'de zaten not edilmişti, hâlâ geçerli):** Checkout
  akışı bu adreslerden SEÇİM YAPMAYA hâlâ bağlanmadı - `PlaceOrderCommand` her siparişte hâlâ
  serbest metin alanlarından YENİ bir adres oluşturuyor. Bu, checkout formunun kendisini
  ("kayıtlı adreslerimden birini seç" / "yeni adres gir" ayrımı) yeniden tasarlamayı gerektiren
  AYRI ve daha büyük bir karar - bu turun kapsamı yalnızca bağımsız adres yönetimiydi.
- 2 yeni regresyon testi (ekle/varsayılan yap/sil akışı - ikinci bir adres varsayılan yapılınca
  ilkinin bayrağının otomatik kalktığı dahil; başka bir müşterinin adresine erişim/silme/varsayılan
  yapma denemesinin REDDEDİLDİĞİ) VE gerçek çalışan Storefront'a karşı canlı HTTP ile (iki adres
  eklenip sırayla varsayılan yapılarak yalnızca BİRİNİN varsayılan kaldığı, biri silinip diğerinin
  durduğu, İKİNCİ bir gerçek müşteri hesabıyla BİRİNCİNİN adresini silmeye çalışmanın sessizce
  reddedildiği - 500 değil, adresin sahibinde SAĞLAM kaldığı - kanıtlandı) doğrulandı. Toplam 117
  test (13+3+101).

## Bülten Kaydı (Misafirler İçin, Çift Onaylı)

`NewsletterSubscriber.Confirm()` de kapatıldı - Faz 0/1'den beri domain'de vardı ama Application
katmanının HİÇBİR yerinden çağrılmıyordu; Storefront'ta misafirler (üyelik gerektirmeden) için bir
bülten kayıt formu HİÇ yoktu (yalnızca giriş yapmış müşteriler için ayrı bir `Customer.
NewsletterSubscribed` bayrağı vardı, bkz. "Favoriler (Wishlist) ve Bülten Aboneliği" bölümü -
ikisi birbirinden BAĞIMSIZ mekanizmalar, kasıtlı olarak birleştirilmedi).

- Footer'a herkese açık bir "Bültene Abone Ol" formu eklendi. Yeni `SubscribeToNewsletterCommand`
  bir `NewsletterSubscriber(email)` kaydı oluşturup (`IsConfirmed=false`) mevcut e-posta
  altyapısıyla (`IEmailSender`/`EmailTemplate`/`NotificationLog` - `PlaceOrderCommand.
  SendOrderConfirmationEmailAsync`'teki AYNI desen: şablon yoksa varsayılan metne düşer, gönderim
  hatası ana akışı bozmaz) bir onay linki "gönderir"; yeni `ConfirmNewsletterSubscriptionCommand`
  linkteki ID ile `IsConfirmed`'i `true` yapar.
- Aynı e-postayla İKİNCİ kez kayıt olmaya çalışmak yinelenen bir satır OLUŞTURMAZ (var olan,
  henüz onaylanmamış kaydın ID'si yeniden kullanılır); zaten ONAYLANMIŞ bir e-posta sessizce hiçbir
  şey yapmaz (hem gereksiz e-postayı önler hem "bu e-posta bizde kayıtlı mı" sorgusuyla e-posta
  numaralandırmasına kapı aralamaz).
- **Önemli mimari not:** e-posta gönderimi bu projede HİÇBİR ZAMAN gerçek bir SMTP sağlayıcısına
  bağlı değildir (`SmtpEmailSender` yalnızca loglar - bkz. §11/plan). Bu yüzden onay linkindeki
  `SubscriberId` test/smoke sırasında doğrudan DB'den (`NotificationLog`/`NewsletterSubscriber`
  tablolarından) okunur - gerçek bir e-posta kutusuna asla ihtiyaç duyulmaz.
- 2 yeni regresyon testi (kayıt+onay akışı, çift kayıt denemesinin yinelenmediği; geçersiz bir ID
  ile onayın başarısız döndüğü) VE gerçek çalışan Storefront'a karşı canlı HTTP ile (footer
  formundan gerçek bir e-postayla kayıt olunup `NotificationLog`'da başarılı bir "gönderim" kaydı
  oluştuğu, DB'den okunan GERÇEK ID ile onay linkinin ziyaret edilip `IsConfirmed=true` olduğu,
  geçersiz bir ID'nin "Onay Bağlantısı Geçersiz" mesajı gösterdiği) doğrulandı. Toplam 119 test
  (13+3+103).

## Varsayılan Ödeme/Kargo Sağlayıcısı (Checkout Ön-Seçimi)

`IntegrationProvider.MarkAsDefaultForCategory`/`UnmarkAsDefaultForCategory` de kapatıldı - devamı
58'in taramasında bulunan, "düşük değerli bir UX inceliği" diye ertelenen son öğeydi. Aynı anda
yalnızca BİR sağlayıcı bir kategoride (ödeme/kargo/pazaryeri/e-fatura) varsayılan olabilir.

- Admin `/integrations` ekranındaki her AKTİF sağlayıcı kartına "Varsayılan Yap" butonu eklendi;
  varsayılan olan kartta "Varsayılan" rozeti görünür. Yeni `SetIntegrationProviderDefaultCommand`
  aynı kategorideki ESKİ varsayılanın bayrağını otomatik kaldırır (Adres Defteri'ndeki "aynı anda
  tek varsayılan" mantığıyla AYNI desen).
- Checkout formundaki ödeme/kargo radio butonları artık varsayılan sağlayıcıyı ÖN-SEÇİLİ gösterir
  (`GetActiveProvidersQuery`'nin DTO'suna eklenen `IsDefault` alanı üzerinden) - birden fazla aktif
  sağlayıcı varken müşterinin her seferinde bilinçli bir seçim yapması gerekmez, istenirse yine
  değiştirebilir.
- 1 yeni regresyon testi (varsayılanın tek bir sağlayıcıda kaldığı, ikincisi işaretlenince
  ilkinin kalktığı, checkout'un kullandığı sorgunun AYNI bayrağı yansıttığı) VE gerçek çalışan
  Admin+Storefront'a karşı canlı HTTP ile (Admin'de bir sağlayıcı varsayılan yapılıp "Varsayılan"
  rozetinin göründüğü, gerçek bir sepetle checkout sayfası açılınca o sağlayıcının radio butonunun
  `checked` geldiği) doğrulandı. Toplam 120 test (13+3+104).

**Bu, devamı 58'in "son orphaned-entity taraması"ndan çıkan TÜM bulguların kapandığı noktadır**
(devamı 58 Customer.ChangeGroup + ölü kod, devamı 59 Adres Defteri, devamı 60 Bülten Kaydı, devamı
61 Varsayılan Sağlayıcı).

## Teklifi Siparişe Dönüştürme (B2B)

`Quote.ConvertToOrder(Guid orderId)` de kapatıldı - kullanıcıya doğrudan soruldu ("teklif siparişe
dönüşünce oluşan Order nasıl davranmalı?") ve **"PlaceOrderCommand ile aynı akış"** seçildi: gerçek
bir ödeme/kargo sağlayıcısı üzerinden, checkout'takiyle TIPATIP aynı mekanizma (Provider Registry,
`IPaymentGateway.AuthorizeAsync`, `ICargoProvider.GetRateAsync`, sipariş onay e-postası).

- Admin teklif detay sayfasına (`/accounting/quotes/{id}`) - yalnızca GEÇERLİ ve HENÜZ
  dönüştürülmemiş bir teklifte görünen - "Siparişe Dönüştür" formu eklendi: alıcı bilgisi/adres +
  ödeme/kargo sağlayıcısı seçilip yeni `ConvertQuoteToOrderCommand` çalıştırılır.
- **Cari Hesap → Müşteri köprüsü:** `LedgerAccount.LinkedCustomerId` (bir e-ticaret siparişinden
  otomatik oluşan cari hesaplarda zaten dolu - bkz. `OrderCompletedEventHandler`) BOŞSA (muhasebeci
  tarafından elle açılmış, hiç sipariş vermemiş saf bir B2B cari), `PlaceOrderCommand`'ın misafir
  müşteri deseniyle AYNI mantıkla sentetik bir Customer oluşturulup yeni `LedgerAccount.
  LinkCustomer` ile GERİYE bağlanır - bir SONRAKİ teklif AYNI müşteriyi yeniden kullanır, ikinci bir
  Customer OLUŞTURMAZ. Bilinçli fark: misafir checkout'un varsayılan grubu "Bireysel" iken, bir Cari
  Hesap'tan doğan müşteri BİLİNÇLİ OLARAK "Kurumsal" grubuna atanır.
- **Yapısal bir fark ÖNCEDEN tespit edilip güvenle çözüldü:** bir `QuoteLine` serbest metin
  (`Description`) taşır, gerçek bir `Product`a BAĞLI DEĞİLDİR (B2B teklifler genelde katalogda
  olmayan özel/bespoke kalemler içerir). Dönüştürülen `OrderItem`lar bu yüzden `ProductId` olarak
  `Guid.Empty` kullanır - kod tabanı taranıp `GetOrderByIdQuery`nin (hem Admin hem Storefront sipariş
  detayı) `OrderItem.ProductName`'i DOĞRUDAN okuduğu, hiçbir yerde `Product`a JOIN/lookup yapmadığı
  doğrulandıktan SONRA bu karar verildi - tahmin değil, kod okunarak kanıtlanmış bir güvenlik.
- Yeni `OrderSource.AdminQuote` değeri eklendi (mevcut `Web`/`Mobile`/pazaryeri değerlerinin
  YANINA, var olanları bozmadan) - bir siparişin bir B2B tekliften mi yoksa normal checkout'tan mı
  geldiğini raporlama/filtrelemede ayırt edebilmek için.
- Zaten dönüştürülmüş veya süresi dolmuş bir teklifi TEKRAR dönüştürmeye çalışmak reddedilir.
- 2 yeni regresyon testi (dönüşüm + cari hesaba müşteri bağlanması + AYNI cari hesabın İKİNCİ
  teklifinde müşterinin YENİDEN KULLANILDIĞI; dönüştürülmüş/süresi dolmuş teklifin reddedildiği) VE
  gerçek çalışan Admin'e karşı canlı HTTP ile ("Siparişe Dönüştür" butonu Blazor interaktif olduğu
  için - bkz. devamı 14/15/57/58'deki aynı sınırlama - gerçek komut çağrısıyla simüle edilerek;
  teklif sayfasının "Dönüştü" durumuna geçtiği, gerçek sipariş detay sayfasının doğru kalem/toplamı
  (750 ₺ x 3 = 2.250,00 ₺) gösterdiği, cari hesabın DB'de gerçekten bir müşteriye bağlandığı)
  doğrulandı. Toplam 122 test (13+3+106).

**Artık BİLİNÇLİ ertelenmiş öğe yalnızca BİR tane kaldı:** Pazaryeri bounded context'i +
`Order.SetMarketplaceReference` (plan §9, opsiyonel faz + gerçek API kimlik bilgisi gerektiriyor -
ödeme/kargo sağlayıcılarındaki AYNI gerekçe, kullanıcıdan gerçek bir pazaryeri hesabı/API kimlik
bilgisi gelmeden ilerletilmeyecek).

## Storefront Görsel Yeniden Tasarımı (dekorras.com/Journal3 tasarım paritesi)

Kullanıcı mevcut Storefront'un görsel tasarımının ("hiçbir ekran istediğim gibi olmamış") kendi
şirketinin GERÇEK, canlı sitesiyle (dekorras.com — OpenCart 3.0.3.2 + Journal3 teması) **tamamen
aynı** olmasını istedi. Bu bir rakip sitesini kopyalama değil, AYNI şirketin eski platformdan yeni
platforma (bu proje) marka/tasarım sürekliliği taşıma işi. Kullanıcı kapsam derinliğini **"Tam
özellik paritesi (mega menü, zoom, sonsuz kaydırma dahil)"** olarak seçti. **Bağlayıcı kısıtlama:**
dekorras.com'dan hiçbir görsel/metin indirilip kopyalanmadı — yalnızca gerçek canlı HTML/CSS'ten
(`Invoke-WebRequest` ile ham kaynak çekilip) çıkarılan renk/font/yapı/davranış eşleştirildi; tüm
gerçek içerik (kategori/ürün/banner görseli) projenin kendi Admin yükleme ekranlarından gelir.
Hiç build aracı (npm/Sass/bundler) eklenmedi — saf CSS custom property + vanilla JS + CDN `<link>`
(Google Fonts, Bootstrap Icons `cdn.jsdelivr.net`) kullanıldı, mevcut "hiç build aracı yok" kısıtı
korundu.

**Çıkarılan gerçek tasarım token'ları:** ana vurgu rengi `#E96631`/koyu `#DD4B0F` (canlı sitede 117+
kez kullanılıyor), Montserrat (başlık) + Lato (gövde) fontları, sticky header'ın scroll'da 90px'ten
50px'e küçülmesi — hepsi `wwwroot/css/variables.css`'e `:root` custom property olarak taşındı.

6 fazda uygulandı, her faz gerçek dev DB'ye (`_DesignPreviewSeedScratch.cs` — iş bitince silindi)
seed edilen kategori/ürün verisiyle canlı HTTP/tarayıcı testiyle doğrulandı:

1. **Token sistemi + header/footer iskeleti** — `variables.css`→`base.css`→`layout.css`→
   `mega-menu.css` sıralı dosyalar; 3 şeritli header (üst bar/logo-arama-hesap/mega-menü satırı,
   `header.js`'te passive scroll listener ile küçülme); `GetStorefrontMenuQuery`'ye `ImageUrl`
   eklendi (migration gerekmedi, `Category.ImageUrl` zaten vardı); mega menü masaüstünde salt CSS
   `:hover`/`:focus-within`, dokunmatik için küçük JS artışı; footer 4 kolona (Kurumsal/Hesabım/
   Blog/Sosyal+Bülten) çıkarıldı.
2. **Anasayfa** — `HomeController` artık `GetStorefrontMenuQuery`yi de çağırıp her üst kategori için
   290x190 kutucuk grid'i render ediyor (`ImageUrl=null` → marka renkli gradient placeholder, KIRIK
   `<img>` YOK — canlı testte özellikle bu senaryo da doğrulandı).
3. **Ürün kartı + Hızlı Bakış (quickview)** — `_ProductGrid.cshtml`'e hover'da görünen buton eklendi;
   yeni `ProductController.Quickview` → `_ProductQuickview.cshtml` partial'ı `quickview.js` ile
   vendored Bootstrap modal'a fetch edilir. **Canlı testte bulunup düzeltilen gerçek hata:** partial
   kendi `Context.Request.Path`'ini (`/Product/Quickview?...`) `returnUrl` olarak kullanıyordu —
   modal'dan Sepete Ekle sonrası müşteri header/footer'sız çıplak bir partial'a yönlendirilecekti.
   Düzeltme: `quickview.js` gerçek sayfa URL'ini query string'te taşıyor, controller
   `Url.IsLocalUrl` ile doğrulayıp `ViewBag.ReturnUrl`'e koyuyor (mevcut `Cart/Add` zaten
   `LocalRedirect` ile güvenli). Uçtan uca canlı HTTP ile doğrulandı (aynı `$session` içinde:
   quickview fetch → Sepete Ekle POST → `/` yönlendirmesi → sepette gerçekten ürün görünüyor).
4. **Ürün detayı + zoom** — thumbnail'ler artık tıklanıp ana görseli değiştiriyor (önceden inert'ti);
   masaüstünde `matchMedia('(hover: hover) and (pointer: fine)')` ile CSS-transform büyüteç
   (`product-zoom.js`), dokunmatikte ana görsele dokunma tam ekran Bootstrap modal açıyor; ★/☆ ve
   ♥/♡ Unicode karakterleri `bi-star-fill`/`bi-star`/`bi-heart-fill`/`bi-heart` ikonlarına çevrildi
   — gerçek bir müşteri hesabıyla (favoriye ekle + onaylı değerlendirme) canlı doğrulandı.
5. **Sonsuz kaydırma** — `CategoryController`/`SearchController`'a `IndexPartial` eklendi (ortak
   sorgu mantığı private `GetProductsAsync` helper'ına çıkarılıp drift önlendi), `_Pagination`
   JS'siz geri düşüş olarak kalıyor. `infinite-scroll.js` `IntersectionObserver` ile sayfa 2+'yi
   fetch edip mevcut `.row[data-product-row]`'a ekliyor — 13 ürünlü bir kategoride page=1/page=2
   arasında SIFIR çakışma ile canlı doğrulandı.
6. **Kalan sayfa cilası + RTL** — Cart/Checkout/Account/Blog/Cms/Compare (14 dosya, ~786 satır)
   incelendi: hiçbiri hardcoded renk/glyph kullanmıyor, hepsi düz Bootstrap sınıflarından yeni marka
   renklerini otomatik miras alıyor. Tek gerçek hata: `Account/OrderDetail.cshtml`'deki Unicode `←`
   (hem tutarsız hem RTL'de yanlış yöne bakacaktı) `bi-arrow-left` + `[dir="rtl"] transform:
   scaleX(-1)` ile düzeltildi. `Cms/Page.cshtml`/`Blog/Details.cshtml`'in `Html.Raw` ile bastığı
   admin içeriği `.dk-cms-content` sınıfına alınıp taşan görsel/tablo için savunma amaçlı CSS
   eklendi (admin'in kendi biçimlendirmesine dokunulmadı). `rtl.css`'e mega-menü mobil akordeon
   girintisi ve galeri büyüteç ikonu için RTL düzeltmeleri eklendi. Arapça'ya geçilip anasayfa/ürün/
   sepet/checkout/hesap/karşılaştır/blog/kategori sayfalarının hepsi `dir="rtl"` ile hatasız
   render edildiği (sunucu loglarında sıfır exception) canlı doğrulandı.

Toplam 122 test (13+3+106) her fazdan sonra ve iş bitince tekrar tekrar çalıştırıldı, hep yeşil.
Bu tamamen görsel/etkileşimsel bir değişiklik olduğu için yeni birim testi eklenmedi — doğrulama
planın kendi disiplinine uygun şekilde gerçek çalışan sunucuya karşı canlı HTTP ile yapıldı.

## Admin Panel Velzon Reskin + dekorras.com Katalog Aktarımı

Kullanıcı iki bağımsız iş istedi: (1) Admin panelin (`Dekorras.Admin`, Blazor Server - şu ana kadar
hiç görsel olarak elden geçmemiş, ham `dotnet new blazor` iskeleti) görselini `admin-demo\` klasöründe
bulunan **Velzon** (themesbrand.com'un ticari Bootstrap 5 admin dashboard şablonu) sistemine göre
yeniden kurmak; (2) dekorras.com'daki (kullanıcının kendi şirketinin GERÇEK, canlı OpenCart mağazası)
gerçek ürün/kategori verisini sisteme otomatik aktarmak. Plan Mode ile (Explore ajanları + kullanıcıya
katalog kapsamı sorusu - "Tüm katalog" seçildi) planlandı.

### İş 1 — Velzon Reskin (TAMAMLANDI)

Velzon'un statik varlıkları (`assets/{css,js,libs,fonts,images}`, 867 dosya, ~32MB) doğrudan
`Dekorras.Admin/wwwroot/assets/`e kopyalandı (build aracı gerekmedi). `Components/App.razor`'ın
`<head>`'i Velzon'un CSS/JS sırasına (`bootstrap.min.css`→`icons.min.css`→`app.min.css`→
`custom.min.css` + `bootstrap.bundle.min.js`/`simplebar`/`node-waves`/`feather-icons`/`app.js`)
güncellendi - eski `lib/bootstrap` referansı KALDIRILDI (versiyon çakışması riski). `MainLayout.razor`
Velzon'un `#layout-wrapper > #page-topbar + .app-menu.navbar-menu + .main-content` iskeletine
yeniden kuruldu (sahte demo veri - dil seçici/bildirim/mesaj/"apps" dropdown'ları - BİLİNÇLİ OLARAK
eklenmedi, yalnızca gerçek olan: hamburger toggle + oturum açan kullanıcının e-postası + Çıkış Yap).
`NavMenu.razor` Velzon'un iç içe `<ul>/<li>` + Bootstrap `collapse` desenine yeniden yazıldı - MEVCUT
tüm 11 link (Modül Seçimi/Entegrasyonlar/CMS 3 alt link/E-posta Şablonları/Sistem 5 alt link)
KORUNDU, yalnızca "İçerik Yönetimi"/"Sistem Yönetimi" başlıkları altında görsel olarak gruplandı -
yeni bir hedef/link EKLENMEDİ.

Kalan **44 sayfanın hepsi** (3 paralel arka plan ajanına dağıtılıp) aynı 3 mekanik kuralla
güncellendi: (a) çıplak `<h1>` → Velzon'un `page-title-box` deseni, (b) çıplak `<table>` →
`.card > .card-body > .table-responsive.table-card > table.table-nowrap.align-middle`, (c) düz
`badge bg-X` → Bootstrap 5.3 "subtle" varyantı (`badge bg-X-subtle text-X`). **Hiçbir `@code` bloğu,
`@page`/`@attribute`/`@inject` direktifi veya form alanı DOKUNULMADI** - saf markup/CSS sınıfı
değişikliği. Login/2FA/Error/NotFound sayfalarının başlıkları BİLİNÇLİ OLARAK dokunulmadan bırakıldı
(auth kutusu içi başlık, sayfa başlığı deseni değil). Sonradan bulunan tek gerçek atlanan durum
(`Integrations.razor`'daki `StatusBadgeClass()` C# metodunun döndürdüğü string literal'lar - ajan
`@code`'a dokunmama kuralına haklı olarak uydu ama sonuç eksik kaldı) elle düzeltildi.

Gerçek admin@dekorras.com girişiyle canlı HTTP doğrulandı: shell (topbar/sidebar/footer) + tüm
varlıklar (CSS/JS, fingerprint'li) doğru yükleniyor, Modül Seçimi/Ürünler/Kategoriler/Siparişler/
Cari Hesaplar/Raporlar/Entegrasyonlar sayfalarının hepsi 200 dönüyor ve yeni `page-title-box`/
`table-card` markup'ı doğru render ediliyor. 122 test (13+3+106) değişmeden yeşil kaldı (saf görsel
iş, yeni test eklenmedi).

### İş 2 — Katalog Aktarımı (dekorras.com → gerçek dev DB)

Yeni bağımsız konsol projesi: `backend/tools/Dekorras.CatalogImporter` (Persistence+Application+
Infrastructure'a referans, `Dekorras.slnx`'e eklendi - bu repodaki İLK CLI/konsol-runner, önceki
emsal yalnızca entegrasyon testlerinin "doğrudan handler çağır" deseniydi, DI container'sız).

**Kazıma stratejisi (uygulama sırasında keşfedilip iyileştirildi):** OpenCart'ın SEO slug'larına HİÇ
gerek duyulmuyor - "ham" route'lar (`index.php?route=product/category&path={id}`,
`index.php?route=product/product&product_id={id}`) doğrudan çalışıyor, kategori/ürün ID'leri sayfa
gövdesindeki class'lardan ve "İlgili Kategoriler" filtre panelinden (`data-filter-trigger name="c"
value="{id}"` + `count-badge`) çıkarılıyor. Ürün alanları (ad/açıklama/fiyat/SKU/marka/görsel/stok
durumu) HTML seçicileriyle DEĞİL, sayfadaki standart `application/ld+json` `"@type":"Product"`
bloğundan okunuyor - çok daha güvenilir ve kırılgan olmayan bir kaynak (AngleSharp bağımlılığı bu
yüzden hiç gerekmedi, kaldırıldı - yalnızca `Regex` + `JsonDocument`). Kendi slug'larımız üretiliyor
(`{isim-slug}-{opencart-id}`) - kaynağın SEO yapısına bağımlı olunmuyor, hem benzersizlik garanti
ediliyor hem kaynakla eşleşme kolaylaşıyor. **İdempotent:** her kategori/ürün oluşturmadan önce
slug'a göre DB'de aranıyor, varsa atlanıyor - araç yarıda kesilip güvenle TEKRAR çalıştırılabilir.
Saygılı kazıma: istekler arası 300-800ms rastgele gecikme, gerçekçi User-Agent, 3 deneme + üstel
geri çekilme, `import-log.txt`'ye + konsola satır satır ilerleme.

`Category.SetImage(url)` domain'de vardı ama hiçbir komut sarmıyordu - `AddProductImageCommand`
ile AYNI desende yeni `SetCategoryImageCommand`/Handler eklendi.

**Küçük bir alt kategoriyle (15 ürün) uçtan uca duman testi yapılıp DOĞRULANDI** (kategori+ürünler+
gerçek indirilen görseller DB'de ve `%LOCALAPPDATA%\Dekorras\uploads\product-images\`te doğru
göründü), test verisi temizlendi, SONRA tam 4 kök kategoriyle (Duvar Kaplamaları/Duvar Kağıtları/
Aksesuarlar/Posterler) gerçek çalıştırma başlatıldı. **Gerçek keşfedilen ölçek plan tahmininden
BÜYÜK çıktı:** yalnızca Duvar Kağıtları kategorisi tek başına ~1976 benzersiz ürün içeriyordu.

**TAMAMLANDI:** ~2 saat süren çalıştırma başarıyla bitti - **25 kategori, 1999 ürün (2000 işlenenden
yalnızca 1 tanesi kaynak sitenin aynı SKU'yu iki farklı üründe kullanması nedeniyle atlandı), 1999
gerçek indirilmiş ürün görseli, 2 marka** gerçek dev DB'ye eklendi. Storefront'un (yukarıdaki
"Storefront Görsel Yeniden Tasarımı" bölümünde anlatılan) anasayfa kategori kutucukları, mega menü
ve ürün detay galerisi bu gerçek veriyle canlı test edilip sorunsuz çalıştığı doğrulandı - iki büyük
iş (Storefront tasarımı + katalog aktarımı) birlikte uçtan uca kanıtlanmış oldu.

## Admin Panel Yeniden Yapılandırma — Areas Birleştirme + RBAC + CRUD Ekran Deseni

Kullanıcı canlı Admin panel ekran görüntüsü paylaşıp 4 sorun bildirdi: (1) sol menü giriş
yapılmadan da görünüyordu ve yetkiye göre filtrelenmiyordu, (2) Admin panel AYRI bir uygulama/süreç
olarak KALMAMALI, ASP.NET Core'un gerçek "Areas" kalıbıyla `/admin` altında TEK bir uygulamaya
taşınmalıydı, (3) "SysAdmin" her şeye erişebilmeli, diğer roller yalnızca yetkili oldukları
menülere erişmeliydi, (4) E-Ticaret/Muhasebe alt ekranları (Kategoriler/Ürünler/Cari Hesaplar/vb.)
modül ana sayfasındaki düz buton satırı yerine SOL MENÜDE yer almalı, ayrıca TÜM CRUD ekranlarında
"ekleme" listenin altında inline form yerine listenin sağ üstünde bir "Yeni" butonuyla AYRI bir
sayfaya gitmeli, kayıttan sonra liste ekranına dönmeliydi. İki kritik mimari karar
(AskUserQuestion ile): **"Gerçek birleştirme"** (ters proxy değil - Admin, Storefront ile AYNI
süreçte/portta) ve **"Modül bazlı" yetki granülerliği** (mevcut 2 kaba yetki korunur, SysAdmin
bağımsız olarak her şeye erişir).

### Faz 1-2 — RBAC + Sol Menü Restorasyonu (TAMAMLANDI)

Yeni **"SysAdmin"** rolü `DbInitializer.cs`'e eklendi (idempotent - mevcut gerçek admin hesabına
bile SONRADAN uygulanabilecek şekilde HER PARÇASI ayrı ayrı kontrol edilir, tek bir toplu
"AdminProfile zaten var mı" guard'ına DAYANMAZ - aksi halde zaten var olan gerçek hesaplara asla
uygulanamazdı). `AppUserClaimsPrincipalFactory`'ye ayrı bir `"sysadmin"` claim'i eklendi (rol adı
"SysAdmin" olan kullanıcıya). `ECommerceAccess`/`AccountingAccess` politikaları `RequireClaim`'den
`RequireAssertion`'a çevrilip SysAdmin claim'ini de kabul edecek şekilde genişletildi, yeni
`SysAdminOnly` politikası eklendi.

`NavMenu.razor` tamamen yeniden yazıldı: TÜM menü `<AuthorizeView>` ile sarılı (oturum kapalıyken
YALNIZCA "Giriş Yap" görünür), E-Ticaret/Muhasebe grupları kendi politikalarıyla, "Sistem
Yönetimi" (Roller/Yöneticiler/Denetim Kaydı/İletişim Ayarları) YALNIZCA SysAdmin'e gösterilir.
**Bulunan ve düzeltilen gerçek bir tutarsızlık:** "İki Faktörlü Doğrulama" KİŞİSEL bir güvenlik
ayarı (her admin KENDİ 2FA'sını açar) - başlangıçta yanlışlıkla SysAdmin-only gruba konulmuştu,
fark edilip menüde bağımsız bir üst-seviye linke taşındı. Ayrıca sayfa SEVİYESİNDE de sadece menü
GİZLEME'nin yetmediği görülüp `RoleList`/`RoleEdit`/`AdminUserList`/`AdminUserEdit`/`AuditLogList`/
`ContactSettingsList`'in `@attribute`'ları `[Authorize(Policy = "SysAdminOnly")]`'a yükseltildi
(aksi halde menüde gizli bir sayfaya DOĞRUDAN URL ile erişim engellenemiyordu).

`ECommerceHome.razor`/`AccountingHome.razor`'daki pill-buton satırı TAMAMEN kaldırıldı (navigasyon
artık sol menüde) - bu, ekran görüntüsündeki "boş beyaz kutu" izlenimini de gideriyor (gerçek bir
boş `<div>` yoktu, `page-title-box`'ın kısa başlık+buton arasında bıraktığı Velzon-standart boşluktu,
buton satırının kaldırılmasıyla sayfa artık yalnızca başlık+pano istatistikleri gösteren sade bir
dashboard).

### Faz 3 — CRUD Ekranlarını Liste + Ayrı Ekleme Sayfasına Ayırma (TAMAMLANDI)

`ProductList.razor`/`ProductEdit.razor` deseni (zaten var olan referans) 16 liste sayfasının HEPSİNE
(toplam 19 yeni `{Varlık}Edit.razor` sayfası - bazı sayfalar 2 bağımsız varlık barındırdığı için:
`CashAndBank`→`CashRegisterEdit`+`BankAccountEdit`, `ChecksAndNotes`→`CheckEdit`+
`PromissoryNoteEdit`) 3 paralel arka plan ajanına dağıtılıp uygulandı: Liste sayfası yalnızca tablo +
`page-title-box`'ta bir "+ Yeni {Varlık}" butonu barındırır; yeni sayfa `Update` komutu VARSA dual
route (`/new` + `/{Id:guid}`) ile hem oluşturma hem düzenlemeyi tek bileşende yapar (yoksa yalnızca
`/new`), kayıttan sonra HER ZAMAN listeye döner. **Bilinçli davranış değişikliği:** `QuoteList`
eskiden oluşturduktan sonra Teklif Detayına yönlendiriyordu - artık (kullanıcının "tüm CRUD'da
listeye dön" talimatına uyularak) listeye dönüyor. **Kapsam netliği:** yalnızca ÜST DÜZEY "yeni
{varlık} ekle" akışları taşındı - `ProductEdit`'in kendisi bile videolar/varyantlar/toplu indirim/
grup fiyatı gibi İÇ İÇE alt koleksiyonları KENDİ SAYFASINDA inline bırakıyor, aynı ilke
`QuoteDetail`'in kalem ekleme ve `LedgerAccountDetail`'in hareket ekleme akışlarına da uygulanıp
DOKUNULMADI. `RoleList`/`AdminUserList`'in var olan bir satır için izin/rol atama gibi inline
"yönet" arayüzleri de (bir oluşturma formu OLMADIKLARI için) listede bırakıldı. `CategoryList`
(ağaç yapısı) için yeni sayfa opsiyonel `?parentId=` query param'ı destekliyor ("Alt Kategori Ekle"
linkleri buradan geçiyor).

Gerçek bir kategori oluşturup (`CreateCategoryCommandHandler` doğrudan çağrılarak - Blazor
interaktif buton sınırlaması nedeniyle) listede GERÇEKTEN göründüğü, "Düzenle" linkinin doğru
`/{id}` rotasına gittiği ve düzenleme sayfasının doğru başlığı ("Kategoriyi Düzenle") gösterdiği
canlı HTTP ile doğrulandı.

### Faz 4 — Areas Birleştirmesi: Admin → Storefront'un İçine, `/admin` Altında (TAMAMLANDI, EN RİSKLİ FAZ)

`Dekorras.Admin` (Blazor Server, AYRI bir uygulama/süreç/port) TAMAMEN kaldırılıp
`Dekorras.Storefront`'un (düz MVC) içine `Components/Admin/**` altında taşındı - artık TEK bir
uygulama/süreç/port (5297), `/admin` bir Blazor Server "alanı" (area). **Kritik teknik zorluklar ve
çözümleri:**

- **Route çakışması:** Admin'in TÜM `@page` route'ları (47 sayfa) + tüm `href`/`NavigateTo` literal
  string'leri `/admin` önekini almak zorundaydı (aksi halde Storefront'un KENDİ `/Account/Login`
  controller action'ıyla, ya da hiçbir şeyle çakışmasa bile YANLIŞ konuma giderdi). NavMenu.razor
  zaten GÖRELİ path'ler kullandığı için (`href="ecommerce/categories"`, öndeki `/` YOK) hiç
  değişmedi - `<base href="/admin/">` bunları otomatik doğru çözüyor. Yalnızca MUTLAK (`/` ile
  başlayan) path'ler (liste sayfalarındaki "+ Yeni" butonları, Edit sayfalarındaki `NavigateTo`
  çağrıları, birkaç "Modül Değiştir" linki) `/admin` önekini ELLE almalıydı - bilinen 12 route
  öneki (`/ecommerce`, `/accounting`, `/cms`, `/notifications`, `/system`, `/settings`,
  `/integrations`, `/login`, `/Error`, `/not-found`, `/Account/`) için tam metin eşleştirmesiyle
  TEK bir PowerShell script'iyle 61 dosyada güvenle toplu değiştirildi; `@page "/"` (Home.razor) ve
  3 adet çıplak `href="/"` (kök path) ayrıca elle `/admin`'e çevrildi (bunlar bilinen 12 önekten
  hiçbirine uymuyordu).
- **Aynı paylaşılan çerez şeması, İKİ FARKLI giriş sayfası:** Admin ve Storefront AYNI
  `"Identity.Application"` çerez şemasını paylaşıyor (AYNI `AspNetUsers` tablosu, `AdminProfile`
  varlığı bir kullanıcıyı "admin" yapıyor) - tek bir global `LoginPath` OLAMAZDI. Çözüm: İKİ AYRI
  çerez şeması AÇMAK (Blazor'un `AuthorizeView`'ı tek bir ambient şemaya güvendiği için
  karmaşıklaştırırdı) YERİNE, `ConfigureApplicationCookie`'nin `OnRedirectToLogin`/
  `OnRedirectToAccessDenied` event'leri `context.Request.Path.StartsWithSegments("/admin")`e göre
  DALLANDI - `/admin/**` için `/admin/login`e, geri kalan HER ŞEY için Storefront'un KENDİ
  `/Account/Login`ine. Canlı HTTP ile HER İKİ yönün de DOĞRU çalıştığı (birbirine KARIŞMADIĞI)
  doğrulandı.
- **"auth" rate-limit politikası çakışması:** Storefront'ta ZATEN kendi giriş/kayıt uç noktaları
  için `"auth"` adlı bir politika vardı (devamı 63) - Admin'inki bu isimle birleştirilseydi
  `AddPolicy` ÇAKIŞIRDI (uygulama başlangıçta çökerdi). Admin'in politikası `"admin-auth"` olarak
  yeniden adlandırılıp kendi Account minimal-API endpoint'lerine ayrı uygulandı.
- **Varlık/isim çakışmaları:** Admin'in Velzon varlıkları `wwwroot/assets/` → Storefront'un
  `wwwroot/admin-assets/`'ine taşındı (netlik için, gerçek bir çakışma olmasa da); `app.css`/
  `favicon.png` → `admin-app.css`/`admin-favicon.png`; kök Blazor belgesi (`App.razor` →
  `AdminApp.razor`) içindeki `@Assets[...]` yolları ve `<base href>` (`/` → `/admin/`) buna göre
  güncellendi; CSS izolasyon paketi artık `Dekorras.Storefront.styles.css` (proje-çapında TEK
  paket, Admin'in KENDİ scoped stilleri - MainLayout/NavMenu/ReconnectModal - aynı pakette
  Storefront'un kendi stilleriyle ÇAKIŞMADAN bir arada yaşıyor, CSS izolasyonunun tüm amacı bu).
- **Migration/seed sorumluluğu taşındı:** `DbInitializer.SeedAsync`/`MigrateAsync` çağrısı yalnızca
  Admin'in `Program.cs`'indeydi - Storefront'un KENDİ `Program.cs`'ine taşınmazsa geliştirme
  ortamında migration/seed HİÇ TETİKLENMEZDİ.
- `Dekorras.Admin` projesi TAMAMEN silindi (dosya sistemi + `Dekorras.slnx`), IIS Express
  `applicationhost.config`'teki ayrı "Dekorras.Admin" `<site>`+`<location>` bloğu kaldırıldı -
  Storefront'un sitesi artık hem `/` hem `/admin`i AYNI portta (5297) sunuyor.

**Canlı doğrulama (gerçek IIS Express out-of-process hosting'de, `dotnet run` DEĞİL):**
müşteri-yüzü Storefront (anasayfa/sepet) hiç bozulmadan çalışıyor; `/admin`e oturum kapalıyken
gidiş `/admin/login`e, Storefront'un KENDİ `/Account/Orders`ine gidiş KENDİ `/Account/Login`ine
yönleniyor (birbirine KARIŞMIYOR); gerçek SysAdmin girişiyle `/admin` panosu + E-Ticaret/Muhasebe/
Sistem Yönetimi grupları + `/admin/ecommerce/products`/`/admin/system/roles`/
`/admin/accounting/cash-and-bank` hepsi 200 dönüyor; paylaşılan `/uploads/` görselleri (ürün
fotoğrafları) hâlâ doğru sunuluyor. 122 test (13+3+106) değişmeden yeşil kaldı.

**SONRADAN bulunan ve düzeltilen GERÇEK bir hata (kullanıcı ekran görüntüsüyle bildirdi - Velzon
CSS/JS hiç uygulanmıyordu, sayfa tamamen çıplak HTML görünüyordu):** `AdminApp.razor`'daki
`<base href="/admin/" />` ile `@Assets["admin-assets/css/..."]` gibi ÖNDE "/" OLMAYAN (göreli)
varlık yolları BİRLEŞİNCE, tarayıcı bunu `/admin/admin-assets/css/...` (var OLMAYAN, 404 bir yol)
olarak çözüyordu - gerçek dosya konumu `/admin-assets/...`dı. Bu, yalnızca GERÇEK bir tarayıcının
`<base href>` + göreli yol birleşimini NASIL çözdüğüne bakınca ortaya çıkan bir hataydı - önceki
doğrulama yalnızca "bu MUTLAK URL 200 dönüyor mu" diye kontrol etmişti (`/admin-assets/css/app.min.css`
DOĞRUDAN istendiğinde gerçekten 200 dönüyordu), tarayıcının SAYFA İÇİNDEKİ göreli href'i base href
ile NASIL birleştireceğini hiç TEST ETMEMİŞTİ. Düzeltme: `AdminApp.razor`'daki TÜM `@Assets[...]`
anahtarları BAŞTA "/" ile (mutlak) verildi; ayrıca `ReconnectModal.razor`'ın (proje şablonundan
kopyalanan, Blazor'un yerleşik bir bileşeni DEĞİL) kendi `@Assets["Components/Layout/..."]`
öz-referansı hem eksik "Admin/" klasör segmentini (dosya `Components/Admin/Layout/`'a taşınmıştı)
hem de öndeki "/"i almalıydı. **Ders: `<base href>` özniteliği kök olmayan bir yola ("/admin/" gibi)
ayarlandığında, o belge içindeki HER göreli (öndeki "/" olmayan) href/src TÜM sayfa boyunca yanlış
çözülme riski taşır - yalnızca belirli bir mutlak URL'nin sunucuda var olup olmadığını değil,
GERÇEKTEN RENDER EDİLEN HTML'deki her href/src değerinin base href ile doğru bileşip
bileşmediğini kontrol etmek gerekir** (bu turda tüm href/src'ler regex ile çıkarılıp TEK TEK HTTP
ile doğrulanarak düzeltildi - "bir örnek URL'yi manuel dene" yeterli DEĞİLDİ).

## Dinamik Banner / Sayfa Bloğu Sistemi (BannerZone)

Kullanıcının verdiği "Dinamik Banner Sistemi - Geliştirme Promptu.md" belgesi genel amaçlı bir
şablon olarak yazılmıştı (düz MVC `Areas/Admin`, `int` PK, `ImageSharp`, `IMemoryCache`, sınıf
tabanlı JS undo/redo) - bu projenin GERÇEK mimarisiyle (Onion+CQRS, `Guid` PK,
`TransientTrackingInterceptor`, Blazor Server admin, `IFileStorage`, hiç cache yok, vanilla IIFE JS)
uyuşmuyordu. Belgenin FONKSİYONEL hedefi (sınırsız iç içe satır/kolon ağacı, breakpoint bazlı grid,
sürükle-bırak yeniden sıralama, çoklu içerik tipi, admin'den uçtan uca yönetim) korunarak GERÇEK
konvansiyonlara uyarlandı - `Category`'nin kendine-referans ağaç deseni, `Banner`'ın `IFileStorage`
yükleme akışı ve `MainMenuViewComponent`'in ViewComponent kalıbı BİREBİR takip edildi.

**Mevcut basit `Banner` entity'sine (ImageUrl/LinkUrl/DisplayOrder, tek görsel karusel)
DOKUNULMADI** - yeni sistem tamamen ek bir bounded context. Admin menüsünde "Bannerlar" (eski) ile
"Banner Bölgeleri" (yeni) YAN YANA duruyor. Anasayfa, `home-main` bölgesi BOŞSA/pasifse otomatik
olarak eski karusel'e (ya da hiç banner yoksa mevcut "Dekorras" başlık bloğuna) döner.

**Veri modeli** (`Dekorras.Domain/Content/BannerZone.cs`):
- `BannerZone` (Key/Name/Description/IsActive - Key üzerinde unique index, kod içinde bölgeyi bu
  anahtarla bulur, ör. `"home-main"`).
- `BannerNode` - self-referencing (`Category.ParentCategoryId` ile BİREBİR aynı desen:
  `DeleteBehavior.Restrict`, private `_children` alanı + `SetPropertyAccessMode(Field)`),
  `NodeType` (`Row`/`Column`), `SortOrder`, `Depth`, `Path` (materialized path, Guid'lerle
  `/{id}/{id}/`), `SettingsJson` (düz `string?`, `AuditLog.DetailsJson` ile aynı desen, attribute
  yok). `Reparent(...)` domain metodu.
- `BannerContent` - yalnızca `Column` tipli düğüme bağlanır (handler'da doğrulanır), `ContentType`
  enum'u 10 değer içerir (`Image`, `ImageWithOverlay`, `Heading`, `Text`, `Button`, `LinkList`,
  `Video`, `RawHtml`, `Spacer`, `ProductWidget`), zamanlanmış yayın için `StartDateUtc`/`EndDateUtc`.

**Bilinçli kapsam sadeleştirmeleri** (keşifle doğrulanan "bu projede emsal YOK" bulgularına dayanır,
belgenin orijinal spesifikasyonundan BİLEREK küçültülen kısımlar):
- Görsel yeniden boyutlandırma/WebP dönüşümü yok (ne `LocalFileStorage` ne `AddProductImageCommand`
  bunu yapıyor - aynı temel çizgide kalındı).
- Output Cache/`IMemoryCache` yok (projede hiçbir yerde önbellekleme kullanılmıyor).
- Tam istemci-taraflı undo/redo, taslak/yayın revizyonu, otomatik-kaydet, şablon kütüphanesi, fare
  ile piksel-piksel sürükleyerek yeniden boyutlandırma, canlı breakpoint simülatör iframe'i yok -
  bunun yerine: breakpoint genişlikleri basit sayı girişleri, yeniden sıralama vendored **SortableJS**
  ile (Velzon'un kendi `nestable.init.js` örneğiyle aynı ayarlar), her yapısal aksiyon (satır ekle/
  kolona böl/taşı/sil/ayar kaydet) KENDİ ANINDA MediatR komutu - toplu diff/kaydet yok.
  HTML sanitizasyonu eklenmedi (`CmsPage`/`BlogPost` zaten `@Html.Raw` ile sanitize edilmeden
  render ediliyor - aynı mevcut risk duruşu, yeni bağımlılık eklenmedi).
- `ProductWidget` içerik tipi DAHİL edildi (yüksek değer/düşük efor) - mevcut
  `GetStorefrontProductsQuery` + `_ProductGrid.cshtml` yeniden kullanılıyor, yeni ürün sorgu mantığı
  yazılmadı.

**Application katmanı** (`Dekorras.Application/Content/{Commands,Queries,Models,Helpers}/`):
`CreateBannerZoneCommand`/`UpdateBannerZoneCommand`/`SetBannerZoneActiveCommand`,
`AddBannerRowCommand`/`SplitRowIntoColumnsCommand`/`UpdateBannerNodeSettingsCommand`/
`UpdateBannerNodeAdvancedCommand`/`MoveBannerNodeCommand` (Path-tabanlı alt-ağaca-taşıma koruması:
`newParentPath.StartsWith(movingNode.Path)`)/`DeleteBannerNodeSubtreeCommand` (Path LIKE ile toplu
silme), `AddBannerContentCommand`/`UpdateBannerContentCommand`/`RemoveBannerContentCommand`
(`Banner`'ın `IFileStorage` yükleme deseniyle aynı), `GetBannerZonesQuery`/`GetBannerZoneByIdQuery`/
`GetBannerZoneTreeQuery` (`GetCategoryTreeQuery`'nin yerel özyinelemeli `Map(id)` deseniyle BİREBİR
aynı - tek sorguda düz liste çekilip ağaca dönüştürülür, hem Storefront hem Admin AYNI sorguyu
kullanır). `BannerCssBuilder` (`Dekorras.Application.Content.Helpers`, saf statik sınıf -
`SettingsJson`'dan Bootstrap grid sınıfı + satır-içi CSS üretir, DB'siz birim testli - 10 test).

**Storefront render katmanı:** `BannerZoneViewComponent` (`MainMenuViewComponent` ile birebir
kalıp), `Views/Shared/Components/BannerZone/{Default,_Node,_Content,Empty}.cshtml` (özyinelemeli,
derinlik 6'da kesilir), `wwwroot/css/banner-zone.css` (`variables.css`'in `--dk-*` token'larını
kullanan `.dk-bnr-*` sınıflar). `HomeController`/`Home/Index.cshtml`: `home-main` bölgesi doluysa
`BannerZoneViewComponent` render edilir, boşsa `_HomeHeroFallback.cshtml` (eski karusel/başlık
bloğunun aynen taşındığı hali) devreye girer.

**Admin panel** (`Components/Admin/Pages/Content/BannerZone{List,Edit,Builder}.razor` +
`BannerNodeEditor.razor`): liste + metadata düzenleme sayfaları mevcut CRUD-ayrım desenini izliyor;
`BannerZoneBuilder.razor` asıl ağaç editörü - sol tarafta `BannerNodeEditor`'ın özyinelemeli
render ettiği satır/kolon kartları (SortableJS ile sürükle-bırak, "Kolonlara Böl" hazır şablon
butonları: 12/6+6/4+4+4/3+3+3+3/8+4/4+8/3+6+3), sağ panelde seçili düğüm için Bootstrap pill-tab'lı
(Düzen/Stil/İçerik/Gelişmiş) ayar formu. Her aksiyon kendi anında komutunu çağırıp ağacı yeniden
yüklüyor. `wwwroot/js/admin/banner-zone-builder.js` (vanilla IIFE) SortableJS `onEnd` olayını
`DotNetObjectReference`/`[JSInvokable] OnNodeMoved` ile Blazor'a bağlıyor - bu, projede İLK JS
interop kullanımı (önceki admin sayfalarının hiçbiri JS interop kullanmıyordu).

**Seed:** gerçek dev DB'ye ("Dekorras") gerçek, iç içe bir `home-main` bölgesi seed edildi - 1.
satır 8+4 kolona bölünüp sol kolona gerçek içe aktarılmış bir ürün görseliyle "Görsel + Metin"
kaplaması, sağ kolona 2 alt-satır (her biri tek kolonlu, birer gerçek ürün görseli) eklendi; 2.
satır tam genişlikte "Duvar Kaplamaları" kategorisine bağlı bir Ürün Vitrini (`ProductWidget`)
içeriyor. Seed script'i tek seferlik kullanım için scratchpad'te yazılıp çalıştırıldıktan sonra
silindi - SEED EDİLEN VERİ kalıcı, script'in kendisi değil (`CatalogImporter`'ın aksine, o kalıcı
bir araç olarak `tools/` altında tutuluyor çünkü tekrar tekrar kullanılabilir; bu seed tek seferlikti).

**Doğrulama (gerçek IIS Express hosting'de):** migration (`AddBannerZoneSystem`) gerçek dev DB'ye
uygulandı; `BannerCssBuilder` için 10 birim testi + toplam 132 test (13+13+106) yeşil; anasayfa
gerçek HTML'inde `dk-bnr-zone`/`dk-bnr-overlay`/`dk-bnr-product-widget` sınıfları ve seed edilen
gerçek görseller (`1531.jpg`/`1530.jpg`/`1529.jpg`) doğrulandı; bölge geçici pasife alınıp eski
karusel'e (ya da banner yokken başlık bloğuna) DOĞRU düştüğü, sonra tekrar aktifleştirildiği
kanıtlandı; admin girişi antiforgery token'ı ile GERÇEK bir form POST'u simüle edilerek yapıldı
(`/admin/Account/Login` minimal-API uç noktası `app.UseAntiforgery()` altında olduğu için ham
POST'ta önce `/admin/login` sayfasından `__RequestVerificationToken` çekilmesi GEREKTİĞİ bu turda
öğrenildi), gerçek oturumla `/admin/cms/banner-zones` listesinde "home-main" ve builder sayfasında
seed edilen TAM yapı (4 satır kartı, 5 kolon kartı, xs:8/xs:4 grid rozetleri, 4 içerik çipi -
Görsel+Metin/2 Görsel/Ürün Vitrini) doğrulandı; builder + login sayfalarının render edilen HTML'inden
çıkarılan TÜM href/src (toplam 50) tek tek HTTP ile 200 doğrulandı (bkz. yukarıdaki "base href" dersi
- bu kez baştan doğru uygulandı); `/admin/cms/banner-zones`e girişsiz erişimin doğru şekilde
`/admin/login?returnUrl=...`e yönlendiği (RBAC) doğrulandı.

**Bu turda öğrenilen yeni bir ders:** Admin panelinin minimal-API `Account/Login` uç noktaları
`app.UseAntiforgery()` middleware'i altında - ham bir HTTP POST (tarayıcısız) göndermeden ÖNCE
`/admin/login` sayfasının HTML'inden `__RequestVerificationToken` gizli alanını çekip POST body'sine
eklemek GEREKİYOR, aksi halde 400 Bad Request döner (Storefront'un müşteri girişinde bu adım daha
önce gerekmemişti/farklıydı - bu yüzden ilk denemede atlandı, ikinci denemede düzeltildi).

**SONRADAN bulunan ve düzeltilen GERÇEK bir hata - "BannerZoneBuilder'daki hiçbir buton
çalışmıyor" (kullanıcı ekran görüntüsüyle bildirdi):** Kök neden, `Sortable.min.js` ve
`banner-zone-builder.js` `<script>` etiketlerinin `BannerZoneBuilder.razor`'ın KENDİ markup'ı
içine (sayfaya özel) konmuş olmasıydı. Blazor Web App'in varsayılan istemci-taraflı "enhanced
navigation" özelliği, aynı uygulama içi `<a>` tıklamalarında TAM SAYFA YENİLEMESİ YAPMAZ - yeni
sayfanın HTML'ini fetch ile çekip DOM'u YAMALAR. HTML spesifikasyonuna göre `innerHTML`/fetch
tabanlı DOM ekleme yoluyla gelen `<script>` etiketleri OTOMATİK ÇALIŞTIRILMAZ (yalnızca
tarayıcının kendi HTML ayrıştırıcısının okuduğu, GERÇEK bir tam sayfa yüklemesinde karşılaşılan
script'ler çalışır). Sonuç: kullanıcı bu sayfaya bir link tıklayarak (tam sayfa yenilemesi
OLMADAN) ulaştığında `window.Sortable`/`window.dekorrasBannerBuilder` TANIMSIZ kalıyordu;
`OnAfterRenderAsync`'teki İLK JS interop çağrısı (`JS.InvokeVoidAsync("dekorrasBannerBuilder.
initSortable", ...)`) bir `JSException` fırlatıyordu; Blazor Server, bir yaşam döngüsü
metodundaki YAKALANMAMIŞ istisnayı FATAL sayıp TÜM SignalR circuit'ini düşürüyordu - bu da
sayfadaki HİÇBİR butonun (saf CSS/JS olan Bootstrap dropdown DAHİL, çünkü circuit çöktüğünde
`ReconnectModal`'ın modal `<dialog>`'u devreye girip TÜM sayfadaki tıklamaları TARAYICI
SEVİYESİNDE engelliyor) çalışmamasına yol açıyordu.

**Düzeltme (üç parçalı):**
1. `Sortable.min.js` ve `banner-zone-builder.js` script'leri `BannerZoneBuilder.razor`'dan
   ÇIKARILIP `AdminApp.razor`'ın KALICI `<body>` kabuğuna (bootstrap/simplebar/vb. diğer paylaşılan
   kütüphanelerle AYNI yere) taşındı - bu kabuk yalnızca `/admin` altında bir sayfanın İLK tam
   yüklemesinde bir kez render edilir ve sonraki TÜM enhanced-navigation geçişlerinde DOM'da
   DEĞİŞMEDEN kalır (yalnızca `<AdminRoutes />` içeriği değişir), bu yüzden script'lerin GERÇEKTEN
   çalıştırılmış olması artık HANGİ `/admin` sayfasından girildiğinden BAĞIMSIZ garanti altında.
2. `OnAfterRenderAsync`'teki JS interop çağrısı `try/catch (JSException)` ile sarmalandı - script
   ileride herhangi bir sebeple eksik kalırsa bile sürükle-bırak sessizce devre dışı kalır, TÜM
   circuit ÇÖKMEZ.
3. **Genel sertleştirme:** `BannerZoneBuilder.razor`'daki TÜM ağaç-aksiyonu handler'ları (satır
   ekle/kolona böl/taşı/sil/ayar kaydet) ortak bir `RunTreeActionAsync(Func<Task>)` sarmalayıcısına
   taşındı - `InvalidOperationException`/`KeyNotFoundException`/`FluentValidation.
   ValidationException` (ör. "bu düğüm kendi alt ağacına taşınamaz", derinlik-6 sınırı, kolon
   genişlikleri toplamı >12 gibi GERÇEKTEN oluşabilecek domain kuralı ihlalleri) artık circuit'i
   DÜŞÜRMEK yerine kullanıcıya kapatılabilir bir Türkçe hata uyarısı (`alert-danger`) olarak
   gösteriliyor. Bu, `AddContentAsync`/`SaveContentAsync`'in zaten sahip olduğu try/catch deseninin
   TÜM diğer handler'lara genelleştirilmiş hali.

**Genel ders (Blazor Server + "enhanced navigation" kullanan HERHANGİ bir gelecekteki sayfa için
geçerli):** bir `@page` bileşeninin KENDİ markup'ı içine konan `<script>` etiketleri yalnızca o
sayfaya TAM TARAYICI YENİLEMESİYLE ulaşıldığında güvenilir şekilde çalışır - istemci-taraflı
routing/enhanced-navigation ile ulaşıldığında ÇALIŞMAZ. Bir sayfaya özel JS gerekiyorsa (kütüphane
+ kendi interop dosyası), bunu ya (a) paylaşılan kalıcı kabuğa (`AdminApp.razor`/`App.razor`)
ekle, ya da (b) `OnAfterRenderAsync` içindeki her JS interop çağrısını `try/catch (JSException)`
ile sarmalayarak eksik script'in TEK bir özelliği bozmasına izin ver ama TÜM circuit'i
düşürmesine İZİN VERME. Doğrulama yöntemi de güncellendi: yalnızca "sayfa 200 dönüyor mu ve doğru
veri içeriyor mu" HTTP kontrolü YETERLİ DEĞİL - bu tür bir hata yalnızca GERÇEK bir tarayıcıda,
GERÇEK bir SignalR circuit'i üzerinden JS interop çağrısı tetiklendiğinde ortaya çıkıyor; ham HTTP
istekleri (bu projede önceki turlarda kullanılan doğrulama yöntemi) SignalR/circuit kurmadığı için
bu sınıf hatayı YAKALAYAMAZ - kullanıcının kendi tarayıcısından bildirdiği geri bildirim bu yüzden
vazgeçilmezdi.

**İKİNCİ, DAHA TEMEL bir hata (kullanıcının GERÇEK tarayıcı DevTools konsol çıktısını paylaşması
üzerine bulundu - yukarıdaki script-taşıma düzeltmesi yeterli OLMADI):** Konsolda
`GET http://localhost:5297/admin/_blazor/initializers 404` ve ardından `blazor.web.js`'in bu 404
yanıtını JSON olarak ayrıştırmaya çalışırken attığı `Uncaught SyntaxError` görüldü. Kök neden çok
daha temeldi: `MapRazorComponents<AdminApp>().AddInteractiveServerRenderMode()` Blazor Server'ın
SignalR hub/framework uç noktalarını (`_blazor/*`, `_blazor/initializers` vb.) HER ZAMAN uygulama
KÖKÜNDE (`/_blazor`) dinler - bu, bir `RouteGroupBuilder`/`MapGroup("/admin")` ile `/admin`
altına TAŞINAMAZ (genel API bunu desteklemiyor). Ama sayfanın `<base href="/admin/">` olması,
Blazor'un KENDİ istemci çalışma zamanının (`blazor.web.js`) bu uç noktaları `<base href>`'e GÖRE
göreli hesaplamasına yol açıyordu - yani tarayıcı `/admin/_blazor/initializers`'ı İSTİYORDU ama
sunucu yalnızca kökte (`/_blazor/initializers`) dinliyordu. Bu, canlı olarak
`Invoke-WebRequest http://localhost:5297/_blazor/initializers` (200) İLE
`Invoke-WebRequest http://localhost:5297/admin/_blazor/initializers` (404) karşılaştırılarak
KANITLANDI. Sonuç: Admin panelindeki circuit, Faz 4 birleştirmesinden (devamı 67) BU YANA HİÇBİR
ZAMAN GERÇEKTEN KURULAMAMIŞTI - önceki TÜM "canlı doğrulama"lar yalnızca ham HTTP GET/POST
kullandığı için (SignalR/circuit hiç kurulmadığından) bu YAKALANAMAMIŞTI; bunu ortaya çıkaran
yalnızca kullanıcının kendi tarayıcısından paylaştığı GERÇEK DevTools konsol çıktısıydı.

**Düzeltme:** `AdminApp.razor`'daki `<base href>` `"/admin/"`den GERÇEK köke (`"/"`) çevrildi -
böylece Blazor'un kendi hub hesaplaması sunucunun GERÇEKTEN dinlediği yerle eşleşiyor. Bunun
karşılığında, `<base href>`'e güvenerek YAZILMIŞ TÜM göreli (öndeki "/" olmayan) bağlantılar
MUTLAK (`/admin/...` önekiyle) hale getirildi: `NavMenu.razor`'daki 28 `NavLink`/`<a>` (dahil
"Modül Seçimi" linkinin `href=""`ı → `href="/admin"`), `RoleList.razor` ve `AdminUserList.razor`'daki
birer çapraz-sayfa linki. `@Assets[...]` çağrıları ZATEN mutlaktı (devamı 68), bu değişiklikten
etkilenmediler. Ayrıca kullanıcının paylaştığı konsol çıktısında görülen İKİ AYRI, Blazor'dan
BAĞIMSIZ, Velzon'un kendi vendored `app.js`/`plugins.js`'inde önceden var olan (Faz Velzon reskin -
devamı 66 - döneminden kalma, bu oturuma kadar fark edilmemiş) gerçek hata da düzeltildi: (1)
`plugins.js`'teki `document.querySelectorAll(...)||...` koşulu bir NodeList'in HER ZAMAN "truthy"
olduğu gerçeğini gözden kaçırıyordu (boş olsa bile) - `.length` kontrolü eklendi, artık yalnızca
sayfada GERÇEKTEN `[data-choices]`/`[data-provider]`/`[toast-list]` elementi varsa (şu an HİÇBİR
admin sayfasında yok) tetikleniyor; (2) `app.js`'in `y()` fonksiyonu
`document.getElementById("two-column-menu").innerHTML=""`i null kontrolü OLMADAN çağırıyordu -
bu projenin düzeni bu elementi HİÇ içermediğinden HER admin sayfa yüklemesinde sessizce
`TypeError` fırlatıyordu (aynı satırdaki `.navbar-menu` kontrolünün YANINDA, tutarsız bir şekilde
kontrolsüz bırakılmış) - aynı güvenli `&&` deseni buraya da uygulandı.

**Genel ders (Blazor Web App'i bir MVC/Razor Pages uygulamasıyla AYNI süreçte, kök-olmayan bir
yol altında - `/admin` gibi - birleştirme senaryosu için KRİTİK, gelecekte benzer bir birleştirme
yapılırsa MUTLAKA kontrol edilecek):** `MapRazorComponents<T>().AddInteractiveServerRenderMode()`
SignalR/framework uç noktalarını HER ZAMAN uygulama KÖKÜNDE dinler, bu genel API ile alt bir yola
taşınamaz. Bu yüzden Blazor bileşenlerinin KENDİ `<base href>`'i UYGULAMANIN GERÇEK KÖKÜYLE
("/") eşleşmelidir - `/admin` gibi bir alt-yol GÖRÜNTÜSÜ istiyorsanız (sayfa rotaları `@page
"/admin/..."` gibi zaten mutlak olabilir), bunu `<base href>` ile DEĞİL, her göreli
bağlantıyı/yönlendirmeyi MUTLAK yazarak (`/admin/...` öneki) elde edin. `<base href>`'i kök-olmayan
bir değere ayarlamak yalnızca STATİK varlık yollarını DEĞİL, Blazor'un KENDİ SignalR circuit
kurulumunu da bozar - bu, devamı 68'deki (yalnızca statik CSS/JS varlıkları etkileyen) hatadan
DAHA CİDDİ bir hata sınıfıdır çünkü sonucu "sayfa çıplak görünüyor" değil, "sayfa GÖRÜNÜŞTE doğru
ama HİÇBİR ŞEY TIKLANMIYOR" şeklinde ortaya çıkıyor - yalnızca gerçek tarayıcı DevTools konsolunda
görülebilir, ham HTTP doğrulamasıyla ASLA yakalanamaz.

**ÜÇÜNCÜ bulgu - `<base href>` düzeltmesi bile YETERLİ OLMADI, gerçek engel IIS Express'in kendisi
çıktı:** Kullanıcının paylaştığı bir SONRAKİ konsol çıktısında `blazor.web.js`'in artık DOĞRU uç
noktayı (`http://localhost:5297/_blazor`, köke göre) hesapladığı ve WebSocket'in GERÇEKTEN
`connected` olduğu görüldü - ama BAĞLANTI ANINDA (1 ms içinde) `WebSocket closed with status code:
1006 (no reason given)` ile düşüp "Failed to start the circuit" hatası veriyordu. 1006 (anormal
kapanma, sebep YOK) sunucunun düzgün bir SignalR/uygulama hatası DEĞİL, TAŞIMA KATMANINDA bir
kesinti olduğunu gösteriyordu. Tanı için uygulama IIS Express'ten çıkarılıp AYNI portta (5297)
doğrudan Kestrel'e (`dotnet run`) alındı - kullanıcı AYNI sayfayı yalnızca yenileyerek test etti:
WebSocket bağlandı VE BİR DAHA HİÇ KOPMADI, circuit tamamen sağlıklı çalıştı. **Bu, sorunun Blazor/
uygulama kodunda DEĞİL, IIS Express'in WebSocket taşımasını (bu proje/ortamda, muhtemelen ANCM
"out-of-process" modunun IIS Express'in kendi WebSocket modülüyle uzun ömürlü bağlantıları düzgün
proxy'leyememesi) doğru şekilde SÜRDÜREMEMESİNDE olduğunu KANITLADI.** Bu oturumdan itibaren admin
panelinin GERÇEK interaktif (Blazor Server) doğrulaması IIS Express YERİNE doğrudan Kestrel
(`dotnet run`) ÜZERİNDEN yapılmalı - IIS Express yalnızca statik/MVC (Storefront) tarafı için
sorunsuz, ama Blazor Server circuit'i (uzun ömürlü WebSocket gerektirdiği için) İÇİN GÜVENİLİR
DEĞİL bu ortamda. (İleride gerçekten IIS Express altında Blazor Server çalıştırmak gerekirse,
`applicationhost.config`'teki `<system.webServer><webSocket>` ayarları ve ANCM hosting modeli
- InProcess/OutOfProcess - detaylı incelenmeli; bu oturumda kök neden KANITLANDI ama IIS Express
tarafı ayrıca DÜZELTİLMEDİ, sadece geliştirme/doğrulama Kestrel'e taşındı.)

**DÖRDÜNCÜ, son bulgu - Kestrel'e geçince circuit sağlıklı çalıştı ama konsolda hâlâ 2 gerçek,
Blazor'dan bağımsız hata kalmıştı (yine kullanıcının DevTools çıktısıyla bulundu):**
1. `app.js`'in `m()` fonksiyonu (`y()`'den AYRI, aynı `#two-column-menu` mantığını TEKRAR eden
   ikinci bir fonksiyon - Velzon şablonunda kod tekrarı) AYNI null-check'siz
   `document.getElementById("two-column-menu").innerHTML=""` çağrısını içeriyordu - devamı 71'de
   yalnızca `y()` düzeltilmiş, `m()` GÖZDEN KAÇMIŞTI. Dosyada bu elemente TOPLAM 16 referans
   olduğu görüldü (`y`/`m` dışında da). Tek tek hepsini null-check ile sarmalamak yerine, KÖK
   NEDENİ (element GERÇEKTEN yok) çözmek için `MainLayout.razor`'a boş/gizli bir
   `<div id="two-column-menu" style="display:none"></div>` eklendi - Velzon'un "twocolumn"/
   "semibox" düzen varyantı için kullandığı bu konteyner bu projede (yalnızca "vertical" düzen
   kullanılıyor) hiç yoktu; var olması TÜM 16 referansı tek seferde güvenli hale getirdi, şablonun
   kendi JS'i hiç değiştirilmeden.
2. `admin-assets/css/app.min.css` içinde, dosyanın konumundan 5 üst dizine çıkıp var OLMAYAN
   `css2`/`css2-1`.../`css2-10` dosyalarına işaret eden 11 adet bozuk `@import url(../../../../../css2...)`
   satırı vardı (Velzon reskin döneminden - devamı 66 - kalma, muhtemelen Google Fonts CSS'inin
   yerelleştirilmesi sırasında yanlış hesaplanmış göreli yol). Bu dosyalar hiçbir zaman var
   olmamıştı (kontrol edildi), sayfa zaten AYNI CSS dosyasındaki yerel `@font-face` kurallarıyla
   doğru render oluyordu (ekran görüntülerinde fontlar sorunsuz görünüyordu) - yani bu 11 satır
   TAMAMEN gereksizdi, silindi.
3. `MainLayout.razor`'daki iki logo linki (`href=""`), `<base href>` "/admin/"den "/"e
   değiştirilince YANLIŞ hedefe (Storefront ana sayfasına) gitmeye başlamıştı - `href="/admin"`
   olarak mutlak hale getirildi.

Tüm bu düzeltmelerden sonra: derleme 0 hata/0 uyarı, 132 test yeşil, Kestrel üzerinde canlı HTTP
doğrulaması (giriş, liste, builder sayfası, ağaç verisi, tüm varlıklar) sorunsuz. **Bu oturumun
en önemli genel dersi:** bir hatanın "düzeltildiğini" düşünmek için tek bir doğrulama katmanı
(ham HTTP) yeterli değildir - gerçek tarayıcı DevTools konsolu, HTTP'nin asla göremeyeceği katmanlı
hataları (base href → SignalR endpoint uyuşmazlığı → IIS Express WebSocket taşıma sorunu → vendored
JS'teki bağımsız küçük hatalar) TEK TEK ortaya çıkardı; her kullanıcı geri bildirimi bir öncekini
DEĞİL, YENİ bir katmanı ortaya çıkardı - bu proje ortamında tarayıcı otomasyonu OLMADIĞI için bu
katmanlı keşif SÜRECİ kullanıcının sabırlı, art arda paylaştığı GERÇEK konsol çıktıları olmadan
MÜMKÜN OLMAZDI.

## BannerZone - Çoklu Görselli Kolonlar İçin Otomatik Slider

Kullanıcı admin builder'da bir kolona ard arda birden fazla "Görsel" (Image) tipi içerik eklediğinde
(ekran görüntüsüyle - aynı kolonda 3 "Görsel" çipi - talep edildi), bunların üst üste tek tek
basılması yerine TEK bir Bootstrap carousel'e (slider) dönüştürülmesi istendi; her slaytın kendi
görseli/bağlantısı/alternatif metni (admin'de o içerik için zaten girilmiş özellikler) AYNEN
korunmalı, tek görsel varsa (veya başka bir içerik tipiyse) davranış DEĞİŞMEMELİ.

Uygulama: `Views/Shared/Components/BannerZone/_Node.cshtml`'de bir kolonun `Contents` listesi
(zaten `SortOrder`'a göre sıralı geliyor) ARD ARDA gelen `ContentType.Image` çalıştırmalarına
("run") gruplanır - bir grup 2+ öğe içeriyorsa yeni `_ImageSlider.cshtml` partial'ına (bir
`IReadOnlyList<BannerContentDto>` alır, `_Content.cshtml`'in "Image" dalıyla BİREBİR aynı
görsel/`<picture>`/link mantığını her slayt için tekrarlayan bir Bootstrap carousel üretir -
`carousel-indicators`/`carousel-inner`/prev-next kontrolleri, `id`'si ilk içeriğin Guid'inden
türetilir) yönlendirilir; tek öğeli bir "grup" (veya Image DIŞI herhangi bir tip) her zaman
`_Content.cshtml`'e (mevcut, değişmeyen tekli-görsel/başlık/buton/vb. davranış) gider. Ard arda
OLMAYAN (araya başka bir içerik tipi giren) Image'ler AYRI gruplar sayılır - kasıtlı olarak
karıştırılmaz, admin'in belirlediği sıra bozulmaz. `wwwroot/css/banner-zone.css`'e `.dk-bnr-slider
img` eklendi (anasayfanın kendi `.dk-hero` carousel'iyle aynı `border-radius` token'ı).

Canlı doğrulama: gerçek admin builder'dan bir kolona 3 gerçek görsel eklenip anasayfada bunun TEK
bir `dk-bnr-slider` (3 `carousel-item`, 3 indikatör, doğru sıralı gerçek görsel URL'leri) olarak
render edildiği, diğer tekli-görsel kolonların (`dk-bnr-image`, sayısı değişmeden 3) ETKİLENMEDİĞİ
canlı HTTP ile doğrulandı.

**Sonrasında bulunan GERÇEK bir sorun (kullanıcı ekran görüntüsüyle bildirdi - eklenen görseller
minicik ve düzensiz görünüyordu):** Kök neden render KODUNDA değil, kullanıcının admin builder'da
deneme yaparken oluşturduğu VERİDE idi - slider'ı barındıran kolona "Düzen" sekmesinden
`col-lg:2/xl:1/xxl:1` gibi son derece dar değerler girilmişti (12 birimlik gridde 1-2 birim =
sayfanın ~%8-16'sı), üstüne aynı satırda hiç içeriği olmayan boş bir alt-satır (3 içeriksiz kolon)
bırakılmıştı, ve bir kolonun görseli hiç yüklenmemiş (ImageUrl null) haldeydi. Bunları
`UpdateBannerNodeSettingsCommand` (dar kolonu `colXs:6` ile normalize et)/
`DeleteBannerNodeSubtreeCommand` (boş satırı sil)/`RemoveBannerContentCommand` (boş görseli sil)
komutlarıyla doğrudan (bir önceki seed script'indeki gibi tek seferlik, DI'sız bir çalıştırmayla)
düzeltildi. AYRICA, benzer durumların GELECEKTE de "çirkin" görünmesini önlemek için KOD tarafında
iki kalıcı sağlamlaştırma yapıldı: (1) `banner-zone.css`'te `.dk-bnr-image`/`.dk-bnr-overlay-wrap`/
`.dk-bnr-slider` görsellerine `object-fit:cover` + `min-height:120px`/`max-height:480px` eklendi -
farklı en-boy oranlı GERÇEK ürün görselleri (ki bu proje hep GERÇEK, birbirinden farklı boyutlu
görseller kullanıyor) hangi kolon genişliğine düşerse düşsün artık kırpılıp DÜZGÜN bir kutuda
görünüyor, minicik/deforme kalmıyor; (2) `_Content.cshtml`'in Image/ImageWithOverlay dallarına ve
`_ImageSlider.cshtml`'e "ImageUrl boşsa o içeriği/slaydı HİÇ render etme" koruması eklendi - admin
bir içerik oluşturup görseli SONRADAN yükleyecekse artık kırık `<img>` yerine o içerik sessizce
atlanıyor.

## Üç Parçalı Storefront İyileştirmesi (Metin Bindirmesi + Anasayfa Blokları + Header Reskin)

### 1) BannerZone "Görsel" içerik tipi için metin bindirmesi (renk + hizalama)

`BannerContentSettings`'e `AltTextColor`/`AltTextAlign` ("left"/"center"/"right") eklendi (mevcut
`TextColor` alanından BİLİNÇLİ OLARAK ayrı tutuldu - o alan Heading/ProductWidget'ta farklı anlamda
kullanılıyor). `BannerCssBuilder.BuildImageOverlayStyle` (+ 6 yeni birim testi) bu ayarlardan
`color`+`justify-content`+`text-align` içeren satır-içi bir style üretir.
`Views/Shared/Components/BannerZone/{_Content,_ImageSlider}.cshtml`'in "Image" dalında,
"Alternatif Metin" DOLUYSA görselin üzerine `.dk-bnr-image-text-overlay` (`banner-zone.css`'e
eklendi - `ImageWithOverlay`'in karartmalı `.dk-bnr-overlay`'inden BİLİNÇLİ OLARAK farklı: burada
arka plan karartması YOK, yalnızca konumlandırılmış metin) katmanı basılır - HTML `alt` özniteliği
kullanımı DEĞİŞMEDİ, bu ayrıca görünen bir katman. Admin'de `BannerZoneBuilder.razor`'ın içerik
ekleme/düzenleme panelinde (hem yeni hem düzenleme formunda), yalnızca `ContentType.Image` iken
"Alternatif Metin" alanının HEMEN ALTINDA küçük bir "Metin Bindirme Stili" alt-bölümü (renk
seçici + hizalama `<select>`) eklendi - kapsam yalnızca düz "Görsel" tipiyle sınırlı, "Görsel +
Metin" (`ImageWithOverlay`, zaten kendi Title/Subtitle mekanizmasına sahip) DOKUNULMADI. Yeni
`AddBannerContentCommand`/`UpdateBannerContentCommand` parametresi EKLENMEDİ - zaten var olan
`SettingsJson` alanı üzerinden taşınıyor (ProductWidget'ın izlediği AYNI desen).

### 2) Anasayfa: kategoriye özel modüler bloklar (tüm-ürün listesi kaldırıldı)

Anasayfanın alt kısmındaki eski, filtresiz "Ürünler" (24 ürün, tüm kategoriler karışık) bölümü
TAMAMEN kaldırıldı. `Category` entity'sine `IsFeaturedOnHomepage` bayrağı (+ `SetFeaturedOnHomepage`
domain metodu, migration `AddCategoryIsFeaturedOnHomepage`) eklendi - yalnızca admin'in AÇIKÇA
işaretlediği (ve hâlâ aktif olan) kategoriler anasayfada birer "blok" olarak görünür.
`SetCategoryFeaturedOnHomepageCommand` (`SetCategoryActiveCommand` ile BİREBİR aynı desen) ve
`GetFeaturedHomepageCategoriesQuery` (yeni) eklendi. `HomeController.Index()` artık TEK bir
filtresiz ürün sorgusu YERİNE, öne-çıkan HER kategori için AYRI bir `GetStorefrontProductsQuery`
çağrısı yapıyor (ProductWidget banner içeriğinin `_Content.cshtml`'deki BİREBİR aynı çağrı deseni -
yeni bir ürün sorgu mantığı YAZILMADI), sonucu `HomeCategoryBlock` (yeni, `Models/HomeCategoryBlock.cs`)
listesi olarak view'a taşıyor. `Views/Home/Index.cshtml` bu listeyi, üstteki kategori fayans-grid
bölümüyle AYNI üslupta (`_ProductGrid` partial'ı yeniden kullanılarak) render ediyor. Hiç ürünü
olmayan bir öne-çıkan kategori (boş sonuç) blok olarak GÖRÜNMEZ, sayfa çökmez. Admin tarafında
`CategoryTreeNode.razor`'a (mevcut Aktifleştir/Pasife Al butonunun YANINA) "Anasayfada Göster/Gizle"
butonu + "Anasayfada" rozeti eklendi; `GetCategoryTreeQuery`'nin `CategoryTreeItemDto`'suna
`IsFeaturedOnHomepage` alanı eklendi.

**Bilinçli not:** BannerZone'un ZATEN var olan "Ürün Vitrini" (ProductWidget) içerik tipi de
kategoriye özel ürün bloğu gösterebiliyor (builder'dan manuel sürükle-bırak ile) - bu YENİ mekanizma
onun YERİNE GEÇMİYOR, ONUNLA YAN YANA duruyor: ProductWidget "banner ağacının bir parçası olarak
serbestçe konumlandırılmış" bir blok isterken, `IsFeaturedOnHomepage` "kategori düzenleme
ekranından tek tıkla, anasayfanın ALT kısmında sabit sırayla" bir blok istiyor - iki farklı
kullanım senaryosu, aynı alttaki `GetStorefrontProductsQuery`/`_ProductGrid` mekanizmasını paylaşıyor.

### 3) Header/mega-menü reskin (yalnızca CSS - yapı DEĞİŞMEDİ)

Keşifte doğrulanan kritik bulgu: header/mega-menü YAPISI (`_Layout.cshtml`, `MainMenu/Default.cshtml`)
ZATEN hover ile açılan tam bir mega-panel sistemiydi (`.dk-mega-item`/`.dk-mega-panel`,
`mega-menu.css`) - "düz metin link listesi" değildi, yalnızca RENK/vurgu eksikti. Bu yüzden HİÇBİR
Razor/HTML değişikliği YAPILMADI, yalnızca `layout.css`/`mega-menu.css` düzenlendi:
`.dk-mega-nav` arka planı `variables.css`'teki (dekorras.com'dan çıkarılmış GERÇEK) marka
turuncusuna (`--dk-color-primary`) çevrildi; `.dk-mega-nav .dk-mega-link` (hem açılır panelli hem
yaprak linkler) beyaza çevrildi; `.dk-mega-item > .dk-mega-link::after` ile yalnızca açılır paneli
olan öğelere (mevcut markup zaten bu ayrımı `.dk-mega-item` class'ıyla sağlıyor) bir aşağı ok (▾)
eklendi; üst satırdaki "Hesabım"/"Sepetim" linkleri dolgulu/pill turuncu butonlara dönüştürüldü
(Bootstrap'in `.text-dark` utility'sini override etmek için bilinçli bir `!important` istisnası).
"Karşılaştır" linki kaldırılmadı, yalnızca görsel ağırlığı azaltıldı. Mega panelin kendi İÇ
linkleri (kategori çocukları) ayrı bir class (`.dk-mega-panel-col a`) kullandığı için bu
değişikliklerden ETKİLENMEDİ, beyaz-üstüne-beyaz riski YOK - hem masaüstü (panel `background:
var(--dk-color-bg)`, beyaz) hem mobil (akordeon modunda panel AYNI beyaz arka planı korur)
doğrulandı.

**Doğrulama:** `dotnet build`/`dotnet test` (138 test - 6 yeni `BuildImageOverlayStyle` testi dahil)
yeşil; migration gerçek dev DB'ye uygulandı; Kestrel'de canlı HTTP ile: gerçek bir Görsel içeriğe
renk (`#ffcc00`)+sağ hizalama ayarlanıp `.dk-bnr-image-text-overlay` satır-içi style'ının
(`color:#ffcc00;justify-content:flex-end;text-align:right;`) doğru üretildiği; 2 gerçek kategori
("Duvar Kaplamaları", "Posterler") anasayfada gösterilecek işaretlenip eski düz "Ürünler"in
KAYBOLDUĞU, yerine 2 yeni `<h2>` başlıklı gerçek ürün bloğunun GERÇEK ürün verisiyle göründüğü;
`layout.css`/`mega-menu.css`'teki turuncu arka plan/pill buton/ok karakteri kurallarının
render edilen CSS'te MEVCUT olduğu doğrulandı (gerçek görsel/tarayıcı testi bu ortamda mümkün
değil - kullanıcı geri bildirimi beklenecek, bu oturumun standart yöntemi).

## Admin Panel Liste Sayfaları - jQuery DataTables'a Geçiş

Kullanıcı admin panelindeki TÜM listeleme sayfalarının arama/sıralama/sayfalama'ya kavuşmasını
istedi ve AskUserQuestion ile GERÇEK jQuery DataTables.js'i (Blazor-native bir alternatif yerine)
tercih ettiğini AÇIKÇA belirtti - bu, projenin şu ana kadarki "jQuery yok" konvansiyonundan
BİLİNÇLİ bir sapma, kullanıcının kendi seçimi. Gerçek jQuery (3.7.1) ve DataTables (1.11.5 çekirdek
+ Bootstrap 5 entegrasyonu + Responsive eklentisi) diskte ZATEN mevcuttu - jQuery `wwwroot/lib/
jquery/` altında (ASP.NET Core iskelesinden kalma, hiç kullanılmamış), DataTables ise önceki Velzon
reskin oturumunun kaynak indirmesinde (`C:\E-Ticaret-Yeni-Sistem\admin-demo\themesbrand.com\`) -
YENİ bir indirme/CDN bağımlılığı GEREKMEDİ, `wwwroot/admin-assets/libs/{jquery,datatables}/`e
kopyalandı. Dışa aktarma (CSV/Excel/PDF/yazdırma) eklentisi BİLİNÇLİ OLARAK dahil EDİLMEDİ -
istenmedi, ek karmaşıklık (pdfmake+font dosyaları) gerektirirdi.

**Kapsam dışı bırakılan 4 sayfa (zorla tabloya çevirmek MEVCUT kullanılabilirliği BOZARDI):**
`CategoryList.razor` (özyinelemeli ağaç bileşeni), `BannerList.razor` (görsel kart-grid'i),
`RoleList.razor` (izin matrisi kartları), `ContactSettingsList.razor` (düz bir ayar formu, hiç
`<table>` içermiyor - dönüşüm sırasında keşfedildi). Geri kalan **18 sayfa** (19 tablo -
`EmailTemplateList.razor`'ın 2 tablosu var) gerçek `<table>` kullanıyordu ve dönüştürüldü.

**Blazor Server'ın DOM yeniden render'ı vs DataTables'ın DOM sahipliği çakışması** (bu oturumda
SortableJS ile YAŞANAN ve yukarıda belgelenen dersle AYNI risk sınıfı) `wwwroot/js/admin/
datatable.js`deki `window.dekorrasDataTable.init(tableId, lastColumnIsActions)` ile çözüldü - her
başlatmadan ÖNCE var olan bir DataTable örneği varsa `.DataTable().destroy()` ile yok edilip
SIFIRDAN kurulur (Sortable.js için zaten kanıtlanmış "yok et - yeniden kur" deseni). 18 sayfadaki
TEKRAR EDEN `IJSRuntime`/`OnAfterRenderAsync`/`JSException` try-catch kalıbı, yeni bir görünmez
paylaşılan bileşende (`Components/Admin/Shared/DataTableScript.razor`) TEK YERDE toplandı - her
sayfa yalnızca `<DataTableScript TableId="..." ReloadToken="_alanAdi" />` satırını ekliyor.
`ReloadToken` olarak sayfanın kendi liste alanı verilir (her `LoadAsync()` MediatR'dan YENİ bir
referans döndürdüğü için bu referans değişimi doğal bir "yeniden yüklendi" sinyali).

Her dönüştürülen tabloda: sabit bir `id`, ve tarih/para sütunlarında `data-order` özniteliği
(DataTables'ın varsayılan METİN sıralaması "9,00" > "10,00" gibi YANLIŞ sonuç verirdi - ham
sayısal/ISO-8601 değer `data-order`a, biçimlendirilmiş görüntü metni AYNEN kalıyor). Son sütunu
işlem butonu OLMAYAN sayfalarda (`BrandList` gibi) `LastColumnIsActions="false"` geçilerek o
sütunun yanlışlıkla sıralanamaz/aranamaz yapılması ÖNLENDİ.

**Doğrulama:** `dotnet build`/`dotnet test` (138 test) yeşil; Kestrel'de canlı HTTP ile TÜM 18
sayfanın render edilen HTML'inde tablo `id`sinin varlığı, `<thead>`/ilk `<tbody>` satırının sütun
sayılarının EŞİT olduğu (doğru bir regex ile - ilk denemede "<thead>" etiketinin kendisinin
"<th" ile başlaması nedeniyle YANLIŞ bir "uyuşmazlık" alarmı verdiğim, kaynağı doğrudan dosyadan
doğrulayıp kendi test script'imin hatası olduğunu KANITLADIĞIM bir metodoloji notu - bkz. yukarıki
benzer "Türkçe karakter encoding" dersleri, bu da AYNI sınıf bir kendi-testini-yanlış-yorumlama
hatası) ve tüm yeni kütüphane dosyalarının (jQuery, DataTables çekirdek/bootstrap5/responsive,
`datatable.js`) 200 döndüğü doğrulandı. `CmsPageList`/`BlogPostList`/`EmailTemplateList` sayfaları
şu an dev DB'de hiç kayıt olmadığı için (mevcut, önceden var olan "@if (_x.Count == 0) boş mesaj
göster" deseni gereği) tabloyu HİÇ render ETMİYOR - bu bir hata DEĞİL, veri eksikliği.
**Sınırlama:** DataTables'ın GERÇEK arama/sıralama/sayfalama davranışı yalnızca gerçek bir
tarayıcıda görülebilir - bu ortamda tarayıcı otomasyonu yok, kullanıcı geri bildirimi gerekiyor.

## Kategoriler de DataTables'a Katıldı (Ağaç → Girintili Düz Tablo)

Kullanıcı "kategoriler neden dahil değildi" diye sorunca, önceki turda BİLİNÇLİ OLARAK kapsam dışı
bırakılan `CategoryList.razor` (özyinelemeli `CategoryTreeNode` ağaç bileşeni) da dönüştürüldü -
ama DOĞRUDAN bir `<table>`e çevirmek yerine, hiyerarşiyi KORUYAN bir tasarım seçildi: `GetCategoryTreeQuery`nin
döndürdüğü ağaç, ÖN-SIRALI (pre-order) gezinerek DÜZ bir listeye çevrilir (ebeveyn her zaman
çocuklarından hemen önce gelir - tablonun VARSAYILAN sırası hiyerarşiyi korur), "Ad" sütununa
derinliğe göre `padding-left` (girinti, METİN İÇİNE değil - arama kutusu yalnızca GERÇEK adı görsün
diye) ve yeni bir "Üst Kategori" sütunu eklenerek hiyerarşi GÖRSEL OLARAK korunur. `CategoryTreeNode.razor`
artık kullanılmadığı için TAMAMEN SİLİNDİ (bu proje konvansiyonu - kesin ölü kod tutulmaz).
Kullanıcının kendi sözleriyle uyarıldığı gibi: DataTables'ın kendi sütun-başlığı sıralaması (ör.
"Ad"a göre alfabetik) hiyerarşik görünümü GEÇİCİ bozabilir - başlığa 3. kez tıklayınca (asc→desc→
özgün sıra döngüsü, DataTables'ın kendi varsayılan davranışı) hiyerarşiye geri dönülür, ayrıca
sayfanın kendi açıklama metnine bu NOT eklendi.

**Bu turda bulunan GERÇEK, önceki turlarda YAZILMIŞ bir hata:** girinti hesaplaması
`@(1 + row.Depth * 1.5)rem` idi - `1.5 * derinlik` bir `double` ürettiğinde ve KÜLTÜRE duyarlı
`ToString()` ile render edildiğinde, uygulamanın Türkçe kültüründe ondalık ayıracı VİRGÜL olduğu
için (`"2,5rem"`) GEÇERSİZ bir CSS değeri üretiyordu - tarayıcı bunu SESSİZCE yok sayıyor, derinlik-1
satırları HİÇ girinti almıyordu (yalnızca tam sayıya denk gelen derinlikler - 0 ve 2 - "1rem"/"4rem"
olarak DOĞRU görünüyordu, bu yüzden ilk bakışta fark edilmesi zaman aldı). Düzeltme:
`.ToString(CultureInfo.InvariantCulture)` eklendi - bu, `data-order` özniteliklerinde ZATEN
uygulanan AYNI dersin (kültüre duyarlı ondalık formatlaması makine-okunur değerlerde HER ZAMAN
`InvariantCulture` gerektirir) BAŞKA bir yerde tekrar keşfedilmiş hali. Projede bu deseni kullanan
BAŞKA bir yer (`ProductEdit.razor`'daki `@(option.Depth * 16)px` gibi) TAM SAYI çarpımı olduğu için
(ondalık üretmiyor) ETKİLENMEDİĞİ doğrulandı.

**Doğrulama:** `dotnet build`/`dotnet test` (138 test) yeşil; Kestrel'de canlı HTTP ile 25
kategorinin TAMAMININ (3 derinlik seviyesi dahil) doğru girintiyle, doğru "Üst Kategori" değerleriyle
render edildiği, önceki turda "anasayfada göster" işaretlenen 2 kategorinin (Duvar Kaplamaları,
Posterler) rozetlerinin GÖRÜNDÜĞÜ, ve TÜM varlıkların (106 referans) 200 döndüğü doğrulandı.

## Kategoriler: Hiyerarşi Sırası Düzeltmesi + Kriter Bazlı Filtreleme

Kullanıcı bir önceki turun ("Kategoriler de DataTables'a katıldı") sonucunu test edince listenin
"karışık" göründüğünü bildirdi. **Gerçek kök neden:** DataTables, HİÇBİR `order` seçeneği
verilmezse KENDİLİĞİNDEN ilk sütuna (Ad) göre alfabetik sıralar - bu, sunucu tarafında özenle
kurulan üst-alt ön-sıralı (pre-order) hiyerarşi sırasını sayfa açılır açılmaz ANINDA bozuyordu.
Bu, yalnızca STATİK HTML/render kontrolüyle (bu ortamdaki tek doğrulama yöntemi) YAKALANAMAYAN bir
sınıf hataydı - DataTables'ın kendi JS'i çalışıp tabloyu YENİDEN sıraladıktan SONRA ortaya çıkıyor,
sunucunun ürettiği HTML'in kendisi HER ZAMAN doğruydu.

**Düzeltme:** `wwwroot/js/admin/datatable.js`e `preserveServerOrder` parametresi eklendi -
`true` ise DataTables'a `order: []` geçilir (ilk açılışta HİÇ sıralama uygulanmaz, sunucunun
render ettiği satır sırası AYNEN korunur). `DataTableScript.razor`ya karşılık gelen
`PreserveServerOrder` bool parametresi eklendi (varsayılan `false` - diğer 18 sayfa ETKİLENMEDİ,
onlarda alfabetik/kod-bazlı varsayılan sıralama zaten makul bir davranış). `CategoryList.razor`
`PreserveServerOrder="true"` geçiyor. Kullanıcı yine de bir sütun başlığına tıklayarak
istediği an sıralayabilir - DataTables'ın kendi asc→desc→özgün-sıra 3-tıklama döngüsü sayesinde
başlığa 3. kez tıklanınca hiyerarşik görünüme geri dönülür (sayfanın açıklama metnine bu not
eklendi).

**Kriter bazlı filtreleme (yeni özellik):** tablonun üstüne 3 filtre `<select>`i eklendi - Durum
(Aktif/Pasif), Anasayfa (Anasayfada Gösterilenler/Gösterilmeyenler), Üst Kategori (Yalnızca Kök
Kategoriler / belirli bir üst kategorinin ADI - listedeki GERÇEK üst kategori adlarından
otomatik türetilir). Bunlar DataTables'ın kendi `.column(index).search(regex, true, false).draw()`
API'sine (`datatable.js`e eklenen `filterColumn` fonksiyonu, TAM EŞLEŞME regex'i ile) doğrudan JS
interop ile bağlanır - sunucu round-trip'i GEREKMEZ (veri zaten tarayıcıda yüklü), DataTables'ın
KENDİ serbest-metin arama kutusuyla YAN YANA, tamamlayıcı olarak çalışır. "Filtreleri Temizle"
butonu (herhangi bir filtre aktifken görünür) üç sütun filtresini de tek seferde sıfırlar.

**Doğrulama:** `dotnet build`/`dotnet test` (138 test) yeşil; Kestrel'de canlı HTTP ile filtre
`<select>`lerinin ve "Yalnızca Kök Kategoriler" seçeneğinin render edildiği, girinti değerlerinin
(1rem/2.5rem/4rem - virgül REGRESYONU olmadan) doğru kaldığı, TÜM varlıkların (106 referans) 200
döndüğü doğrulandı. **Sınırlama (bu oturumda TEKRARLANAN bir gerçek):** `PreserveServerOrder`ın
GERÇEKTEN sıralamayı koruduğu ve filtre dropdown'larının GERÇEKTEN tıklandığında tabloyu filtrelediği
yalnızca GERÇEK bir tarayıcıda JS çalıştırılarak doğrulanabilir - bu ortamda tarayıcı otomasyonu
yok; ancak bu turun KÖK NEDEN teşhisi ve DÜZELTMESİ, DataTables'ın KENDİ belgelenmiş/standart API
davranışına (`order: []`, `column().search()`) dayandığı için yüksek güvenle doğru olduğu
değerlendiriliyor - kullanıcı geri bildirimi yine de nihai doğrulama olacak.

## "Anasayfada Göster/Gizle" Çalışmıyor Şikayeti - İki GERÇEK Kök Neden Bulundu

Kullanıcı, kategoriler için "anasayfada göster/gizle" özelliğinin anasayfada işe yaramadığını
bildirdi. Araştırma, İKİ AYRI, her ikisi de gerçek olan kök nedeni ortaya çıkardı:

**1) `GetStorefrontProductsQuery`'nin kategori filtresi yalnızca TAM eşleşen kategoriye DOĞRUDAN
atanmış ürünleri buluyordu, alt kategorilerini KAPSAMIYORDU.** Bir admin bir ÜST/kök kategoriyi
"anasayfada göster" olarak işaretlediğinde, o kategorinin ürünleri asıl ALT kategorilerinde
kayıtlıysa, `HomeController`'ın kategori başına ürün sorgusu SIFIR sonuç dönüyor ve blok SESSİZCE
hiç render edilmiyordu - bayrağın "hiçbir şey yapmıyormuş" gibi görünmesine yol açan gerçek bir
mantık hatası. **Düzeltme:** `GetStorefrontProductsQueryHandler`'a `ResolveCategoryAndDescendantIds`
yardımcı metodu eklendi - kategori filtresi artık VERİLEN kategorinin KENDİSİ + TÜM alt ağacını
kapsıyor (`GetCategoryTreeQuery`'nin düz-çek-bellekte-gez desenindeki AYNI fikir, BFS ile).
Gerçek veriyle doğrulandı: "decowall-duvar-kagidi-253" kategorisinin yalnızca 1 DOĞRUDAN ürünü var
ama 2 alt kategorisinde (astor: 41, armani: 62) 103 ürün daha var - düzeltmeden ÖNCE sorgu yalnızca
1 ürün dönerdi, SONRA 104 ürünün TAMAMINI (doğru karışık sırayla) kapsıyor. Bu değişiklik
`GetStorefrontProductsQuery`'nin TÜM çağıranlarını (anasayfa blokları, BannerZone'un Ürün Vitrini
içerik tipi, kategori gezinme sayfası) İYİLEŞTİRİYOR - bu, e-ticarette standart/beklenen davranış
(bir üst kategoriye göz atan müşteri alt kategori ürünlerini de görmeyi bekler).

**2) DataTables'ın "responsive" eklentisi, dar ekranlarda çok-butonlu "İşlemler" sütununu
KLONLAYARAK ayrı bir DOM'a taşıyordu - bu klonlar Blazor'un `@onclick` olay yönlendirmesine BAĞLI
DEĞİLDİ, bu yüzden tıklamalar HİÇBİR ŞEY yapmıyordu.** Bu, bir önceki turda ("Admin Panel Liste
Sayfaları - jQuery DataTables'a Geçiş") `responsive: true` seçeneğiyle etkinleştirilmiş, o zaman
FARK EDİLMEMİŞ bir yan etkiydi - yalnızca GERÇEK bir tarayıcıda, GERÇEK bir dar viewport'ta ortaya
çıkan bir sınıf hata (statik HTML doğrulaması bunu asla YAKALAYAMAZDI, DataTables'ın kendi JS'i
render SONRASI DOM'u değiştiriyordu). **Düzeltme:** `wwwroot/js/admin/datatable.js`den
`responsive: true` seçeneği TAMAMEN kaldırıldı, `AdminApp.razor`'dan artık kullanılmayan
`dataTables.responsive.min.js` script referansı silindi - zaten HER sayfada mevcut olan Bootstrap
`.table-responsive` sarmalayıcısı (yatay kaydırma) dar ekran sorununu DOM'u KLONLAMADAN çözüyor.
Bu, yalnızca "Anasayfada Göster/Gizle"yi DEĞİL, dar ekranlarda o AYNI geniş "İşlemler" sütunundaki
TÜM 19 dönüştürülmüş tablodaki TÜM butonları etkileyebilecek bir sınıf hatayı da önlüyordu.

**Doğrulama:** `dotnet build`/`dotnet test` (138 test) yeşil; gerçek veriyle doğrudan sorgu
çağrısıyla alt-kategori kapsamının çalıştığı KANITLANDI (1→104 ürün); Kestrel'de canlı HTTP ile
`dataTables.responsive.min.js`e artık GERÇEK bir `<script>` referansı OLMADIĞI (yalnızca
`<ImportMap />`'ın otomatik varlık listesinde - yürütülmeyen, zararsız bir kayıt olarak - göründüğü,
bu ayrımın regex ile önce YANLIŞ ALARM verdiği ama doğrudan doğrulamayla ayıklandığı) doğrulandı,
TÜM varlıklar (105 referans) 200 döndü. **Sınırlama:** admin panelindeki GERÇEK buton tıklamasının
artık çalıştığı yalnızca GERÇEK bir tarayıcıda doğrulanabilir - bu ortamda tarayıcı otomasyonu yok;
ancak kök neden (DOM klonlama) kanıtlanmış, belgelenmiş bir DataTables davranışı olduğu ve düzeltme
o davranışı TAMAMEN devre dışı bıraktığı için yüksek güvenle doğru olduğu değerlendiriliyor.

## "Anasayfada Göster/Gizle" HÂLÂ Etkisiz Görünüyor - ÜÇÜNCÜ Gerçek Kök Neden

Kullanıcı GERÇEK tarayıcı ekran görüntüleriyle (biri admin Kategoriler sayfası + DevTools konsolu,
biri gerçek anasayfa) şunu gösterdi: yalnızca "Posterler" ve "Duvar Kağıtları" "Anasayfada"
işaretliyken, anasayfanın ÜST kısmındaki kategori KUTUCUK grid'i "Duvar Kaplamaları"nı da (işaretli
OLMAYAN) gösteriyordu - ve bir kategoriyi göster/gizle yapınca anasayfa GÖRÜNÜŞTE değişmiyordu.

**Kök neden:** `Views/Home/Index.cshtml`'in ÜST kısmındaki kategori kutucuk grid'i (mevcut,
`GetStorefrontMenuQuery`'den gelen `menuTree` kullanan, ÖNCEKİ turlarda hiç dokunulmamış bir
bölüm) `IsFeaturedOnHomepage` bayrağını HİÇ KONTROL ETMİYORDU - `menuTree.Where(c =>
c.Children.Count > 0)` yalnızca "çocuğu olan" kategorileri filtreliyordu, işaretli olsun olmasın
FARK ETMEKSİZİN TÜMÜNÜ gösteriyordu. Kullanıcı bunu ALT kısımdaki (gerçekten bayrağa göre
filtrelenen) ürün blok bölümüyle KARIŞTIRDI - "göster/gizle" yaptığında ÜST bölüm hiç
değişmediği için "işlevsellik çalışmıyor" izlenimi oluştu. **Düzeltme:** `HomeController.Index()`
featured kategori id'lerini bir `HashSet<Guid>` olarak `ViewBag.FeaturedCategoryIds`e koyuyor;
`Home/Index.cshtml`'in kutucuk grid döngüsü artık `menuTree.Where(c => c.Children.Count > 0 &&
featuredCategoryIds.Contains(c.Id))` - ARTIK TEK, TUTARLI bir anahtar (aynı bayrak) hem üstteki
kutucuk grid'ini HEM alttaki ürün bloklarını kontrol ediyor. Gerçek veriyle doğrulandı: sayfa
açılışında yalnızca 2 işaretli kategori (Posterler, Duvar Kağıtları - HER İKİSİ İÇİN de hem
kutucuk grid'i HEM ürün bloğu) render ediliyor, "Duvar Kaplamaları"/"Aksesuarlar" (işaretsiz) HİÇ
görünmüyor.

**Ayrıca (kullanıcının paylaştığı DevTools konsolunda görülen, Blazor'dan BAĞIMSIZ, ayrı bir gerçek
hata):** Velzon'un vendored `app.js`'i, bu sadeleştirilmiş admin düzeninde HİÇ bulunmayan (gerçek
Velzon demo'sundaki "Tema Ayarları" offcanvas paneli, bildirim dropdown'ı, sepet özeti gibi)
16 farklı DOM id'sine (`vertical-hover`, `sidebar-size/view/color/img`, `layout-position/width`,
`sidebar-visibility`, `body-img`, `empty-cart`, `checkout-elem`, `notification-actions`,
`notificationDropdown`, `removeNotificationModal`, `delete-notification`, `reset-layout`)
KOŞULSUZ (null kontrolü OLMADAN) erişip `TypeError: Cannot read properties of null` fırlatıyordu -
`#two-column-menu` için devamı 71'de uygulanan AYNI kök nedenin GENİŞLETİLMİŞ hali. Tek tek
yamamak yerine `MainLayout.razor`'a bu 16 id için boş/gizli placeholder `<div>`ler TEK SEFERDE
eklendi - hiçbiri görsel/işlevsel bir şey YAPMIYOR, yalnızca app.js'in çökmesini önlüyor.

**Doğrulama:** `dotnet build`/`dotnet test` (138 test) yeşil; Kestrel'de canlı HTTP ile anasayfada
TAM OLARAK 2 kategorinin (4 `dk-category-section` = 2 kategori × kutucuk+ürün bloğu) göründüğü,
işaretsiz kategorilerin (Duvar Kaplamaları, Aksesuarlar) HİÇ görünmediği, admin panelindeki yeni
placeholder'ların render edildiği, TÜM varlıkların (105 referans) 200 döndüğü doğrulandı.

## Header "Container" Genişliği Yönetim Panelinden Ayarlanabilir Hale Getirildi

Kullanıcı, storefront'un üst bilgi çubuğu (İletişim/Dil/Para Birimi satırı), logo/arama/hesap
satırı ve mega menünün her zaman `container-fluid` (kenardan kenara tam genişlik) olmasından
memnun değildi - geniş monitörlerde header'ın "ortalı ama yine de geniş" görünebilmesini VE bunun
kod değiştirmeden, yönetim panelinden ayarlanabilmesini istedi.

**Uygulama:** Yeni `LayoutSettingKeys` (bkz. `ContactSettingKeys` ile AYNI genel `Setting`
key/value deposu, yeni bir tablo/entity GEREKMEDİ) iki anahtar tanımlıyor:
`Layout.HeaderContainerMode` (`"fluid"` | `"boxed"`) ve `Layout.HeaderContainerMaxWidth`
(ör. `"1600px"`, yalnızca `boxed` modda kullanılır). Yeni admin sayfası
`/admin/settings/layout` (`LayoutSettingsList.razor`, `ContactSettingsList.razor` ile BİREBİR AYNI
"yükle → `@bind` → `UpsertSettingCommand` ile kaydet" kalıbı) bir "Tam Genişlik"/"Ortalı" radio
seçimi ve (yalnızca Ortalı seçiliyken görünen) bir `px` genişlik girişi sunuyor; NavMenu'ye
"İletişim Ayarları"nın hemen altına "Site Genişliği Ayarları" linki eklendi.

`Views/Shared/_Layout.cshtml` her istek başında bu iki ayarı `GetSettingsQuery` ile okuyup TEK bir
`headerContainerStyle` string'i üretiyor: `fluid` modda `null` (Razor'ın koşullu öznitelik
render'ı sayesinde `style` özniteliği HTML'e HİÇ yazılmıyor - mevcut davranış birebir korunuyor),
`boxed` modda `"max-width:{genişlik};margin-left:auto;margin-right:auto;"`. Bu stil, header'ın üç
`container-fluid` sarmalayıcısına da (üst bilgi çubuğu, logo/arama satırı, mega menü) AYNI şekilde
uygulanıyor - `container-fluid`'in kendi responsive padding'i KORUNARAK yalnızca maksimum genişlik
sınırlandırılıyor (Bootstrap'ın sabit breakpoint'li `.container`i yerine ÖZEL/yönetilebilir bir
genişlik - kullanıcının "ortalı ama geniş" isteğine tam karşılık geliyor).

**Doğrulama:** `dotnet build` (0 hata) + `dotnet test` (138 test yeşil) sonrası Kestrel'de canlı
HTTP ile doğrulandı - varsayılan durumda (`Settings` tablosunda kayıt yokken) `style` özniteliği
render edilmiyor (mevcut tam-genişlik davranışı korunuyor); `Settings` tablosuna doğrudan
`Layout.HeaderContainerMode=boxed`/`Layout.HeaderContainerMaxWidth=1500px` yazılıp anasayfa tekrar
çekildiğinde her üç sarmalayıcının da `style="max-width:1500px;margin-left:auto;margin-right:auto;"`
ile render edildiği, test verisi silinince fluid davranışa geri döndüğü doğrulandı. Admin girişi
yapılıp `/admin/settings/layout` gerçek HTTP oturumuyla çekildi, sayfa 200 döndü ve beklenen
Türkçe metinleri (başlık, radio etiketleri) içerdiği doğrulandı.

**Sınırlama:** Gerçek tarayıcıda radio/px girişinin canlı `@bind` davranışı ve kaydet butonunun
SignalR circuit üzerinden çalışması yalnızca kullanıcının kendi tarayıcı testiyle doğrulanabilir -
bu oturumda tarayıcı otomasyonu yok, statik/HTTP doğrulama render edilen HTML'in DOĞRU olduğunu
kanıtlar ama buton tıklamasının uçtan uca çalıştığını KANITLAMAZ (bu sınırlama oturum boyunca
tutarlı şekilde not edildi). **Not:** Kullanıcı bu özelliği KENDİ tarayıcısında gerçekten denemiş
ve "Ortalı"/1600px'i kaydetmiş - `Settings` tablosunda `Layout.HeaderContainerMode=boxed`/
`Layout.HeaderContainerMaxWidth=1600px` olarak gerçek, canlı kullanıcı verisi olarak duruyordu, bu
da kaydet butonunun/circuit'in GERÇEKTEN uçtan uca çalıştığının doğrudan kanıtıdır.

**Devam eden düzeltme (aynı gün) - "px yerine Bootstrap yapılandırmasını liste şeklinde göster":**
Kullanıcı serbest `<input type="number">` px girişini istemedi, Bootstrap'ın KENDİ `.container`
sınıfının resmi breakpoint genişlikleri (`$container-max-widths`: sm 540/md 720/lg 960/xl 1140/
xxl 1320) arasından seçim yapabileceği bir `<select>` istedi. `LayoutSettingKeys` içine
`BootstrapContainerBreakpoints` (5 kayıtlık, etiket+px içeren sabit liste) eklendi,
`LayoutSettingsList.razor`'daki number input bu listeyi dolduran bir `<select @bind="_maxWidthPx">`
ile değiştirildi. `DefaultMaxWidth` da listenin en geniş standart değeri olan `1320px` (xxl) olarak
güncellendi (eskiden rastgele seçilmiş 1600px'ti). Kayıtlı eski bir değer (kullanıcının önceki
1600px'i gibi) listedeki 5 değerden BİRİYLE birebir eşleşmiyorsa, sayfa açılışında en yakın standart
breakpoint'e otomatik yuvarlanıyor (`Math.Abs` farkına göre `OrderBy`) - böylece `<select>` asla
listede olmayan "hayalet" bir değer göstermiyor. Canlı HTTP ile doğrulandı: dropdown 5 seçeneği de
doğru etiketleriyle render ediyor, kullanıcının gerçek 1600px kaydı sayfa açılışında en yakın
standart değere (1320px/xxl) doğru yuvarlanıp seçili geliyor, "boxed" radio'su da doğru işaretli.
Derleme 0 hata, 138 test yeşil.

## Anasayfa Alt Kategori Kutucukları Kaldırıldı + Ürün Listeleme Sayfası Yeniden Tasarlandı

Kullanıcı iki GERÇEK ekran görüntüsüyle iki ayrı düzeltme istedi:

**1) Anasayfa:** "Anasayfada göster" işaretli bir kategori (ör. "Duvar Kağıtları") hem üstte TÜM alt
kategorilerini kutucuk grid'i olarak (`dk-tile-grid`, devamı 77-80'de var olan bölüm) HEM de altta
kendi ürünlerini blok halinde gösteriyordu - kullanıcı SADECE ürünlerin (alt bölüm) görünmesini,
alt kategori kutucuklarının (üst bölüm) TAMAMEN kaldırılmasını istedi. `Views/Home/Index.cshtml`den
tüm `menuTree`/`featuredCategoryIds`/tile-grid `@foreach` bloğu silindi, `HomeController.Index`teki
artık kullanılmayan `GetStorefrontMenuQuery` çağrısı ve `ViewBag.FeaturedCategoryIds` ataması da
kaldırıldı (mega menü kendi `MainMenuViewComponent`si üzerinden AYNI sorguyu bağımsız çalıştırmaya
devam ediyor, ETKİLENMEDİ). Alt kategoriler kendi kategori sayfasından erişilebilir olmaya devam
ediyor, yalnızca anasayfadaki KUTUCUK GÖRÜNÜMÜ kaldırıldı.

**2) Ürün Listeleme Sayfası (Kategori/Arama):** Eskiden sayfanın en üstünde yatay tek satırlık bir
filtre formu (`_ProductFilters.cshtml`) + altında sabit 4 sütunlu bir ızgara + en altta sayfalama
vardı. Kullanıcı üç somut değişiklik istedi: (a) sayfalamanın YUKARIDA, bir araç çubuğunda
görünmesi, (b) o araç çubuğunun yanında Bootstrap mantığıyla 2'li/3'lü/4'lü ızgara + liste/tablo
görünüm anahtarı, (c) sayfanın "solda kriterler, sağda ürünler" standart e-ticaret düzenine
geçmesi.

Uygulama:
- `_ProductFilters.cshtml` yatay formdan DİKEY bir kenar çubuğu kartına (`.dk-filter-sidebar`,
  `position: sticky`) çevrildi - aynı alanlar (Min/Max Fiyat, Marka, Sırala, Sayfa Başına), aynı
  partial adı (çağıran kod değişmedi, yalnızca yerleşimi/CSS'i değişti).
- Yeni `_ProductToolbar.cshtml`: solda sayfalama (`_Pagination` partial'ı BURAYA taşındı,
  `_InfiniteScroll.cshtml`'in altındaki eski kopyası kaldırıldı - artık TEK yerde), sağda iki
  `btn-group`: sütun sayısı (2/3/4) ve görünüm (ızgara/liste, Bootstrap Icons `bi-grid-3x3-gap-fill`/
  `bi-list-ul`).
- `_ProductGrid.cshtml`'in sabit `row-cols-1 row-cols-md-4` sınıfları kaldırılıp `data-cols="@N"`
  özniteliğine çevrildi (`N`, `ViewData["ProductGridDefaultCols"]`'tan okunur, varsayılan 4 -
  Anasayfa blokları/BannerZone ürün widget'ı/Ürün Detayı "Benzer Ürünler" bunu AYARLAMADIĞI için
  eski `row-cols-md-4` görünümüyle BİREBİR aynı kalıyor, hiçbir görsel regresyon YOK). Kategori/Arama
  sayfaları `ViewData["ProductGridDefaultCols"] = 3` ile varsayılanı 3'e çekiyor (kullanıcı isteği:
  "ürünler 3 bloktan oluşacak şekilde").
- `product-grid.css`'e Bootstrap'ın KENDİ `row-cols-*` yüzde mantığını (`50%`/`33.3333%`/`25%`)
  `[data-product-row][data-cols="N"] > .col` seçicileriyle yeniden üreten kurallar + bir
  `[data-view="list"]` override'ı (aynı kart DOM'u `flex-direction: row` ile yatay listeye
  çevriliyor, AYRI bir liste şablonu YAZILMADI) eklendi.
- Yeni `wwwroot/js/product-view.js` (vanilla IIFE, `header.js`/`quickview.js` ile AYNI üslup):
  araç çubuğundaki düğmelere tıklamayı dinler, `[data-product-row]`ın `data-cols`/`data-view`
  özniteliklerini günceller, tercihi `localStorage`da kalıcı tutar. **Kasıtlı kapsam sınırlaması:**
  yalnızca `[data-dk-product-listing]` sarmalayıcısı OLAN sayfalarda (Kategori/Arama) çalışır -
  Anasayfa/BannerZone/Ürün Detayı gibi `_ProductGrid`'in DİĞER kullanım yerlerine DOKUNMAZ, bu
  sayfalar tercihi DEĞİŞTİREMEZ ve etkilenmez (kasıtlı - kullanıcı isteği yalnızca "ürün listeleme
  sayfası" içindi). `infinite-scroll.js` yeni sayfaları AYNI `[data-product-row]` elementine
  ekliyor, dolayısıyla sonsuz kaydırmayla gelen yeni ürünler de mevcut görünüm/sütun tercihini
  otomatik miras alıyor - ekstra bir JS bağlama GEREKMEDİ.
- `Views/Category/Index.cshtml` ve `Views/Search/Index.cshtml` yeni `col-lg-3` (kenar çubuğu) +
  `col-lg-9` (araç çubuğu + ürün ızgarası) satır düzenine geçirildi.

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` 138/138 yeşil. Kestrel'de canlı HTTP ile:
anasayfada `dk-tile-grid` sınıfının hiç render edilmediği, `dk-category-section` sayısının 4'ten
2'ye düştüğü (yalnızca ürün blokları kaldı); gerçek bir kategori sayfasında (`posterler-141`)
`dk-filter-sidebar`/`data-dk-product-toolbar`/`data-dk-cols-group`/`data-cols="3"`/
`product-view.js`/`col-lg-3`/`col-lg-9`/"Kriterler" başlığının hepsinin doğru render edildiği;
arama sayfasında sayfalamanın TEK yerde (`data-dk-pagination-fallback` 1 kez) göründüğü; Anasayfa
ürün bloklarının HÂLÂ `data-cols="4"` ile (yeni araç çubuğu OLMADAN, etkilenmeden) render edildiği;
yeni `product-view.js`/güncellenen `product-grid.css`'in 200 döndüğü doğrulandı.

**Sınırlama:** Liste/ızgara ve sütun sayısı düğmelerinin GERÇEK tıklama davranışı (CSS sınıflarının
anlık değişimi, `localStorage` kalıcılığı) yalnızca gerçek bir tarayıcıda görülebilir - bu oturumda
tarayıcı otomasyonu yok, HTTP doğrulaması yalnızca render edilen HTML'in/varlıkların DOĞRU olduğunu
kanıtlar.

**Kullanıcı talebi üzerine kalıcı süreç kuralı:** Kullanıcı bu düzeltmeyle birlikte her kod
değişikliğinden sonra derleme+test+Kestrel doğrulamasının OTOMATİK yapılmasını (ayrıca
istenmeden) istedi - bu artık kalıcı hafızaya (`feedback_auto_build_test.md`) da kaydedildi.

## Mobil Hamburger Menü Sağdan Açılan Çekmeceye Çevrildi + Banner Düzen Ayarları Bootstrap Listesi

Kullanıcının iki isteği:

**1) Mobil menü:** Eskiden hamburger butonu `#dkMobileMenu`yu navbar'ın HEMEN ALTINA açılan bir
Bootstrap "collapse" paneli olarak genişletiyordu. Artık Bootstrap 5.2+'ın `.offcanvas-lg` sınıfı
kullanılıyor - TEK bir element hem ≥lg (masaüstü, mega menü YATAY/satır-içi, `.offcanvas-header`
gizli) hem <lg (mobil/tablet, GERÇEK offcanvas: backdrop + sağdan `translateX` animasyonu + kapatma
butonu) davranışını karşılıyor - `bootstrap.bundle.min.js`'de zaten gömülü Offcanvas bileşeniyle,
YENİ bir JS yazılmadı. Çekmecenin arka planı `--bs-offcanvas-bg`/`--bs-offcanvas-color` CSS
değişkenleriyle marka rengine (turuncu/beyaz metin) çevrildi ki mevcut `.dk-mega-link{color:#fff}`
kuralı DEĞİŞTİRİLMEDEN çalışmaya devam etsin. Çekmecenin ALTINA (yalnızca mobilde, `d-lg-none`)
telefon/İletişim linki + WhatsApp/Instagram/Telegram sosyal medya ikonları eklendi (mevcut
`ContactSettingKeys` üzerinden - footer'ın ZATEN kullandığı AYNI ayarlar, yeni bir veri kaynağı
GEREKMEDİ).

**2) Banner Düzen ayarları + gerçek mobil uyumluluk sorunu:** Kullanıcı, admin panelindeki banner
kolon genişliği (XS/SM/MD/LG/XL/XXL) alanlarının serbest sayı girişi olmasından ve kullanıcının bu
alanları nasıl dolduracağını bilmemesinden şikayet etti. `BannerZoneBuilder.razor`'daki "Düzen"
sekmesi incelendiğinde bu SADECE bir kullanılabilirlik sorunu değil, GERÇEK bir veri hatasına da yol
açtığı görüldü: canlı `home-main` banner bölgesindeki TÜM 8 kolon `colXs` alanına DOĞRUDAN 4 veya 6
girilmiş, hiçbir daha büyük breakpoint (sm/md/lg/xl/xxl) AYARLANMAMIŞTI - Bootstrap'ın mobil öncelikli
CSS'inde bu, kolonun EN KÜÇÜK telefon ekranında bile 12'de 4 (veya 6) genişliğinde SIKIŞIK kalacağı
anlamına gelir (`col-` sınıfı bir breakpoint'te ayarlanınca daha büyük bir breakpoint tarafından
override edilmediği sürece KALICIDIR) - kullanıcının "mobilde bootstrap yapısına göre uyarla"
şikayetinin GERÇEK kök nedeni buydu.

Düzeltme iki parçalı:
- **Mevcut veri:** `home-main` bölgesindeki 8 kolonun tamamı SQL ile normalize edildi -
  `colXs` değeri `colMd`ye taşındı ve `colXs` NULL'landı (`BannerCssBuilder`de `colXs ?? 12`
  varsayılanı zaten var, yani NULL = "telefon ekranında tam genişlik/12"). Sonuç: `col-12 col-md-4`/
  `col-12 col-md-6` - telefon ekranında ALT ALTA (tam genişlik), tablet (≥768px) ve üzerinde
  amaçlanan 3'lü/2'li düzen. Canlı HTTP ile 8 kolonun TAMAMININ artık `col-12 col-md-*` ürettiği
  doğrulandı.
- **Admin UI:** `BannerZoneBuilder.razor`'ın Düzen sekmesindeki 6 sayı girişi (XS/SM/MD/LG/XL/XXL)
  Bootstrap'ın 1-12 birimlik grid değerlerini listeleyen `<select>` kutularına çevrildi. Her alanın
  etiketinde küçük bir ipucu var: breakpoint'in GERÇEK piksel eşiği (ör. "MD (&ge;768px)") hem
  metin olarak hem `title` tooltip'i olarak gösteriliyor; XS için boş seçenek "Otomatik (12)"
  (varsayılan tam genişlik), diğerleri için "Ayarlanmadı (miras al)" (Bootstrap'ın mobil öncelikli
  kademeli miras alma kuralını AÇIKÇA anlatıyor). Sekmenin en üstüne, bu mantığı bir cümlede özetleyen
  bir bilgi kutusu eklendi - kullanıcının artık "hangi alana ne yazacağım" diye tahmin etmesi
  GEREKMİYOR.

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` 138/138 yeşil. Kestrel'de canlı HTTP ile:
anasayfada `.offcanvas.offcanvas-end.offcanvas-lg`/`btn-close-white`/`dk-mobile-menu-footer`/
`data-bs-toggle="offcanvas"` render edildiği, `home-main` bölgesindeki 8 kolonun HEPSİNİN
`col-12 col-md-{4|6}` ürettiği (`col-4`/`col-6` tek başına HİÇ kalmadığı) doğrulandı. Admin
builder sayfasının "Düzen" seçim kutuları Blazor'un `<select @bind>` + `int?` (nullable numeric)
standart bağlama davranışına dayanıyor - derleme başarılı, ancak bu panel yalnızca bir düğüm
SEÇİLDİKTEN sonra (istemci tarafı etkileşim) göründüğü için statik HTTP anlık görüntüsünde
doğrulanamadı (bu oturum boyunca tutarlı, bilinen bir sınırlama - gerçek tıklama davranışı
kullanıcının kendi tarayıcısında görülebilir).

## Hamburger Menü İkonu Görünmüyordu - GERÇEK Kök Neden Bulundu

Kullanıcı ekran görüntüsüyle mobil görünümde hamburger menü ikonunun HİÇ görünmediğini bildirdi -
buton işlevsel olarak DOM'da vardı (offcanvas'ı tetikliyordu) ama içi TAMAMEN BOŞTU. Kök neden:
Bootstrap'ın standart `.navbar-toggler-icon`'u arka plan SVG'sini `--bs-navbar-toggler-icon-bg` CSS
değişkeniyle çiziyor, ama bu değişken (ve `.navbar-toggler`'ın kullandığı padding/border/focus-width
değişkenlerinin TAMAMI) yalnızca Bootstrap'ın KENDİ `.navbar { --bs-navbar-toggler-icon-bg:...; }`
kural bloğu İÇİNDE tanımlı - bu projenin header'ı (`.dk-header`) gerçek bir `.navbar` sınıflı element
DEĞİL, düz bir flex `<div>`. Sonuç: `var(--bs-navbar-toggler-icon-bg)` TANIMSIZ bir değişkene
başvurduğu için CSS spesifikasyonu gereği geçersiz sayılıyor, `background-image` özelliği başlangıç
değerine (`none`) dönüyor - ikon span'i 1.5em×1.5em'lik boş, görünmez bir kutu olarak kalıyordu.

Düzeltme: `.navbar-toggler-icon` span'i, bu dosyada zaten HER YERDE kullanılan Bootstrap Icons
glifiyle (`<i class="bi bi-list fs-2 text-dark">`) değiştirildi - CDN'den yüklenen bir font-icon
olduğu için `.navbar` CSS değişken bağlamına HİÇ ihtiyaç duymuyor, `text-dark` ile rengi doğrudan
garanti ediliyor. Butona ayrıca `border-0 p-1` eklendi (aynı kök nedenden dolayı geçersiz kalan
border/padding değişkenlerine artık bağımlı değil, görünüm doğrudan utility sınıflarıyla kontrol
ediliyor).

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` 138/138 yeşil. Kestrel'de canlı HTTP ile
butonun artık `bi bi-list fs-2 text-dark` içerdiği doğrulandı.

## Proje GitHub'a Aktarıldı

Kullanıcı projenin `https://github.com/dekorras/E-Ticaret-Platform` (public) reposuna aktarılmasını
istedi. Bu makinede Git/GitHub CLI hiç kurulu değildi - winget ile ikisi de kuruldu, kimlik
doğrulama kullanıcının verdiği hesap şifresiyle DEĞİL (GitHub 2021'den beri git push için şifre
kabul etmiyor), `gh auth login --web` cihaz kodu akışıyla (kullanıcı kendi tarayıcısında onayladı)
yapıldı - hiçbir şifre/token bu oturumda saklanmadı/loglanmadı.

**Push öncesi kapsamlı bir güvenlik taraması yapıldı** (repo public olacağı için kritik):
- `Dekorras38*` (yerel SQLEXPRESS `sa` şifresi) **45 dosyada** düz metin olarak gömülüydü - 40
  entegrasyon test dosyasının her biri kendi `private const string ConnectionString` sabitinde,
  artı `ApplicationDbContextFactory.cs` (EF Core tasarım-zamanı fabrikası),
  `Dekorras.CatalogImporter/Program.cs`, ve iki `appsettings.json` (Api/Storefront). TÜMÜ
  `DEKORRAS_SQL_PASSWORD` ortam değişkeninden okuyacak şekilde değiştirildi (yeni paylaşılan
  `Dekorras.IntegrationTests/TestSqlPassword.cs` yardımcı sınıfı + `ApplicationDbContextFactory`/
  `CatalogImporter`'da doğrudan okuma); bu makinede bu değişken kalıcı (User scope) olarak
  ayarlandı, yerel geliştirme/test akışı HİÇ BOZULMADI (138 test hâlâ yeşil). `appsettings.json`
  (committed) artık `Password=CHANGE_ME` placeholder'ı içeriyor, GERÇEK yerel değer
  `appsettings.Development.json`'a taşındı ve bu dosya `.gitignore`'a eklendi.
  `DbInitializer.SeedAdminPassword` (seed edilen admin panel giriş şifresi, AYNI string değeri
  kullanıyor) ve README'deki dokümantasyonu BİLİNÇLİ olarak DEĞİŞTİRİLMEDİ - bu bir sunucu
  kimlik bilgisi değil, klonlayan herkesin KENDİ yerel veritabanında oluşacak, bilinen bir
  scaffold/demo hesabı (yaygın bir pratik - ör. Django/WordPress'in varsayılan kurulum hesapları).
- **GitHub'ın KENDİ push protection'ı** ilk push denemesini reddetti: vendored (hiçbir Razor
  sayfasından ÇAĞRILMAYAN, ölü) Velzon şablon dosyası
  `wwwroot/admin-assets/js/pages/leaflet-map.init.js` içinde Leaflet/Mapbox örneklerinde YAYGIN
  olarak kullanılan genel bir Mapbox DEMO token'ı (`pk.eyJ1...`, gerçek bir hesaba bağlı değil)
  tespit edildi. Dosya kullanılmadığı doğrulanıp SİLİNDİ (`git commit --amend` ile ilk commit'ten
  de temizlendi - bu commit henüz GitHub'a hiç ulaşmamıştı, amend işlemi kullanıcı onayıyla
  yapıldı).
- `.gitignore`, `bin/`/`obj/`/`appsettings.Development.json`'ın yanı sıra bu depoya AİT olmayan üç
  klasörü de dışladı: `admin-demo/` (Velzon şablonunun ham kaynağı, salt referans), `front-end-demo/`
  (dekorras.com'un tam site aynası - **12,4 GB**, GitHub'a push edilemeyecek kadar büyük ve zaten
  yalnızca tasarım referansı içindi), `hata/` (geçici ekran görüntüsü/hata notu klasörü).

**Sonuç:** Repo `main` dalına tek bir commit olarak push edildi, `.github/workflows/backend-ci.yml`
(önceden hazırlanmış CI - build+test) dahil. `dotnet build`/`dotnet test` (138 test) push öncesi
son kez yeşil doğrulandı.

## Ürün Düzenleme Ekranı Sekmelere Bölündü

Kullanıcı `/admin/ecommerce/products/{id}` sayfasının (Ürün/Görseller/Videolar/İlgili Ürünler/
Varyantlar/Toplu Alım İndirimi/Müşteri Grubu Fiyatları/Özellikler) tek bir uzun dikey kaydırma
sayfası olmasından memnun değildi, her bölümün ayrı bir sekmede gösterilmesini istedi.

`ProductEdit.razor`'daki `<hr>`+`<h4>` ile ayrılmış 8 bölüm, `BannerZoneBuilder.razor`'da ZATEN
kanıtlanmış AYNI sekme kalıbına çevrildi: `nav nav-tabs nav-tabs-custom nav-success` (Velzon'un
kendi "detay sayfası sekmesi" stili) + `data-bs-toggle="tab"` - sekme geçişi Bootstrap'ın KENDİ
JS'i tarafından yapılır (`bootstrap.bundle.min.js` zaten kalıcı admin kabuğunda yüklü), sunucu
round-trip'i GEREKMEZ. `_activeTab` alanı yalnızca hangi sekmenin `active`/`show active` sınıfıyla
BAŞLAYACAĞINI belirler. Yeni bir ürün (`/products/new`) henüz veritabanında olmadığı için (görsele/
varyanta/vs. sahip olamaz) eski `@if (!IsNew)` koruması AYNEN korundu - yalnızca "Ürün" sekmesi
gösteriliyor, diğer 7 sekme yalnızca MEVCUT bir ürün düzenlenirken görünüyor. "Kaydet"/"Vazgeç"
butonları "Ürün" sekmesinin içine taşındı (diğer sekmelerin zaten kendi satır-bazlı Ekle/Kaydet/Sil
butonları var, davranış DEĞİŞMEDİ).

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` 138/138 yeşil. Kestrel'de canlı HTTP ile: gerçek
bir ürün sayfasında 8 sekmenin TAMAMININ (`tab-urun`/`tab-gorseller`/`tab-videolar`/`tab-ilgili`/
`tab-varyantlar`/`tab-toplu-indirim`/`tab-grup-fiyat`/`tab-ozellikler`) render edildiği, yalnızca
"Ürün" sekmesinin başlangıçta `show active` olduğu (toplam 1 eşleşme), yeni ürün ekranında ise
SADECE "Ürün" sekmesinin göründüğü doğrulandı.

**Sınırlama:** Sekmeler arası GERÇEK tıklama geçişi (Bootstrap JS'in DOM'u canlı güncellemesi)
yalnızca gerçek bir tarayıcıda görülebilir - bu oturumda tarayıcı otomasyonu yok.

## Kategoriler İkinci Sekme Oldu + Bağımsız Kaydetme

Kullanıcı, bir önceki sekmeleştirme işleminden sonra "Kategoriler"in de "Ürün" sekmesinin İÇİNDE
(sağ sütunda, form alanlarının arasında) kalmaya devam ettiğini fark edip bunu da AYRI ve İKİNCİ
sırada bir sekmeye almak, ayrıca bunun için "kaydetme işlemi için gerekli yapılandırmayı" istedi -
yani eskiden kategoriler yalnızca ana "Kaydet" (tüm ürünü fiyat/stok/SEO dahil yeniden gönderen)
butonuyla kaydedilebiliyordu, kendi BAĞIMSIZ bir kaydet işlemi yoktu.

**Uygulama:**
- Yeni `SetProductCategoriesCommand` (+ handler, `Product.ProductCategories` koleksiyonunu
  yükleyip `SetCategories(...)` çağırıyor - `UpdateProductCommand`'ın kategori güncelleme mantığıyla
  BİREBİR aynı, `SetProductTrackStockCommand` ile AYNI "tek alanlık bağımsız kaydet" kalıbı).
  Doğrulayıcı `UpdateProductCommand`'daki AYNI kuralı taşıyor: "Ürün en az bir kategoriye
  atanmalıdır."
- `ProductEdit.razor`: "Kategoriler" artık "Ürün"den hemen sonra, İKİNCİ sırada bir sekme
  (`tab-kategoriler`) - kategori onay kutusu ağacı "Ürün" sekmesinin sağ sütunundan buraya
  TAŞINDI. Bu sekme hem YENİ hem MEVCUT ürünlerde görünür (kategori seçimi ürün oluşturmanın bir
  PARÇASI olduğu için "Ürün" sekmesiyle aynı görünürlük kuralına tabi değil, diğer 7 sekmenin
  aksine). MEVCUT bir üründe kendi "Kaydet" butonu var (`SaveCategoriesAsync` →
  `SetProductCategoriesCommand`, "Kategoriler kaydedildi." mesajı gösterir) - tüm ürünü yeniden
  göndermeye GEREK KALMADAN yalnızca kategori ataması güncellenebiliyor. YENİ bir ürün için (henüz
  DB'de yok, bağımsız kaydet ÇALIŞAMAZ) bunun yerine bir bilgi metni gösteriliyor: seçimler "Ürün"
  sekmesindeki ana Kaydet ile (ürün oluşturma isteğinin bir parçası olarak) kaydedilecek.

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` 138/138 yeşil (yeni komutun validasyon/handler
davranışı mevcut `UpdateProductCommand` testleriyle aynı iş kuralını paylaşıyor). Kestrel'de canlı
HTTP ile: sekme sırasının Ürün→Kategoriler→Görseller→... olduğu, gerçek bir üründe kategori
ağacının mevcut atamaları `checked` olarak doğru gösterdiği ve kendi "Kaydet" butonunun render
edildiği, yeni ürün ekranında ise "Kategoriler" sekmesinin GÖRÜNDÜĞÜ ama bağımsız kaydet yerine
bilgi metninin çıktığı doğrulandı.

## Ürünler Sayfasına Kategoriler'deki Filtreleme Mantığı Getirildi

Kullanıcı, admin panelindeki Kategoriler sayfasının filtre-satırı + DataTable görünümünü (bkz.
ekran görüntüsü) Ürünler sayfasına da uygulamamı, ürünlerin kategoriye ve "bir çok özelliğe göre"
filtrelenebilmesini istedi.

**Kritik fark - GERÇEK veri hacmi:** Kategoriler sayfası TÜM satırları (25 kategori) istemciye
yükleyip `dekorrasDataTable.filterColumn` ile İSTEMCİ tarafında filtreliyor - bu 25 satır için
sorunsuz. Ürünler tablosunda ise **1999 gerçek ürün** olduğu (`sqlcmd`ile doğrulandı) görülünce bu
YAKLAŞIMIN BİREBİR kopyalanmasının GERÇEK bir performans sorunu olacağı anlaşıldı: 1999 satırı
(her biri birkaç etkileşimli Blazor butonuyla) tek seferde render edip SignalR üzerinden istemciye
göndermek circuit'i şişirir. Bu yüzden Ürünler sayfasında filtreleme İSTEMCİ tarafında DEĞİL,
SUNUCU tarafında (gerçek bir SQL sorgusunun WHERE koşulları olarak) yapılıyor - DataTables yalnızca
O AN sunucudan çekilmiş sayfanın KENDİ İÇİNDE arama/sıralama sağlıyor.

**Uygulama:**
- `GetProductsQuery` (admin) genişletildi: `Guid? BrandId`, `ProductStatus? Status`, `bool? InStock`
  filtreleri eklendi; `CategoryId` filtresi artık `GetStorefrontProductsQuery`deki AYNI
  "kategori + TÜM alt ağacı" mantığını kullanıyor (bir üst kategori seçilince alt kategorilerdeki
  ürünler de dahil edilir - aksi halde devamı 80'de bulunan AYNI kök nedenin bir başka örneği
  olurdu). Dönüş tipi `IReadOnlyCollection<ProductListItemDto>`den `PagedResult<ProductListItemDto>`
  (`GetStorefrontProductsQuery`de zaten tanımlı, `TotalCount`/`TotalPages` içeren genel sarmalayıcı)
  'a değiştirildi - iki çağıran nokta (`CatalogController`, `ProductEdit.razor`'daki ilişkili ürün
  arama) buna göre güncellendi. `ProductListItemDto`ya `BrandId`/`BrandName`/`CategoryIds` eklendi.
- `ProductList.razor`: Kategoriler sayfasıyla AYNI görsel filtre-satırı üslubu (Durum/Kategori/
  Marka/Stok Durumu + Sayfa Başına), ama her seçim bir SUNUCU round-trip'i (`LoadAsync`)
  tetikliyor - CategoryList'in `dekorrasDataTable.filterColumn` JS çağrılarının AKSİNE. Tablo yeni
  "Kategori" (bir ürünün BİRDEN FAZLA kategorisi olabileceği için virgülle ayrılmış liste) ve
  "Marka" sütunlarını + gerçek bir "Stok Durumu" rozetini kazandı. Gerçek "Toplam N üründen X-Y
  arası" sayacı + Önceki/Sonraki sayfa butonları eklendi (storefront'un `_Pagination.cshtml`
  deseniyle AYNI fikir, Blazor'a uyarlanmış).

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` 138/138 yeşil. Kestrel'de canlı HTTP ile: sayfa
"Toplam 1999 üründen 1-25 arası gösteriliyor" ile DOĞRU render edildi (EF Core'un iç içe koleksiyon
projeksiyonu - `p.ProductCategories.Select(pc => pc.CategoryId).ToList()` - GERÇEK SQL Server'a
karşı hatasız çalıştı), gerçek satırlarda "Kategori" sütununun birden fazla kategoriyi virgülle
doğru gösterdiği doğrulandı.

## BannerZoneBuilder'da Sürükle-Bırak Sonrası Circuit Çökmesi - GERÇEK Kök Neden

Kullanıcı gerçek bir DevTools ekran görüntüsüyle `/admin/cms/banner-zones/{id}/builder` sayfasında
şu hatayı bildirdi: `Error: There was an error applying batch 6` → `Unhandled exception in circuit`
→ `Cannot read properties of null (reading 'removeChild')` - TÜM circuit çöküyor (WebSocket kapanıp
sayfa tamamen tepkisiz kalıyordu).

**Kök neden:** `banner-zone-builder.js`'in SortableJS `onEnd` işleyicisi, kullanıcının GERÇEK
bırakma anında SortableJS'in ZATEN GERÇEK DOM'da FİZİKSEL olarak taşımış olduğu `evt.item`ı
"iyimser bir UI güncellemesi" olarak OLDUĞU GİBİ bırakıp doğrudan `OnNodeMoved`i çağırıyordu.
Blazor'un KENDİ render ağacı bu HARİCİ DOM taşımasından tamamen HABERSİZ - `OnNodeMoved`in
tetiklediği `LoadTreeAsync()` sunucudan yeni ağacı çekip Blazor'a yeniden render ettirdiğinde,
Blazor kendi ESKİ bildiği DOM yapısıyla artık SortableJS tarafından DEĞİŞTİRİLMİŞ gerçek DOM'u
uzlaştırmaya (diff/patch) çalışıyor, bu sırada var OLMAYAN (SortableJS tarafından başka bir yere
taşınmış) bir üst elemente `removeChild` çağırmaya kalkışıp TypeError fırlatıyor - bu, Blazor
Server'ın DOM güncelleme "batch"ini uygulayamamasına ve GÜVENLİK ÖNLEMİ olarak TÜM circuit'i
sonlandırmasına yol açıyordu (tek bir hatalı DOM işlemi bile Blazor Server'da KURTARILAMAZ,
circuit tamamen yeniden başlatılmalıdır - bu yüzden sayfa tepkisiz kalıyordu).

**Düzeltme:** `onEnd` işleyicisi artık `.NET`e haber vermeden HEMEN ÖNCE SortableJS'in yaptığı
GÖRSEL taşımayı `evt.from`/`evt.oldIndex` referans alınarak GERİ ALIYOR - DOM, Blazor'un son
bildiği haliyle AYNEN korunuyor. GERÇEK yeniden sıralama artık YALNIZCA `OnNodeMoved`in tetiklediği
sunucu-onaylı Blazor render'ı (kendi diff'iyle DOM'u güncelleyerek) tarafından yapılıyor - iki
FARKLI mekanizmanın (SortableJS'in kendi DOM manipülasyonu + Blazor'un render'ı) aynı anda AYNI
DOM üzerinde çalışması TAMAMEN ortadan kaldırıldı. Bu, React/Vue gibi diğer VDOM çerçeveleriyle
SortableJS entegre edilirken de kullanılan standart, kanıtlanmış bir kalıptır. `BannerZoneBuilder.razor`'daki
artık geçersiz "SortableJS iyimser bir taşıma bırakır" varsayımını anlatan yorum da güncellendi.

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` 138/138 yeşil. Kestrel'de canlı HTTP ile
kullanıcının GERÇEK banner bölgesi sayfasının (`bcc90e2c-...`) 200 döndüğü ve düzeltilmiş
`banner-zone-builder.js`nin sunulduğu doğrulandı.

**Sınırlama:** Gerçek bir sürükle-bırak işleminin (fare olaylarıyla) çökmeyi ARTIK tetiklemediğini
KANITLAMAK yalnızca gerçek bir tarayıcıda mümkün - bu oturumda tarayıcı otomasyonu yok, kök neden
analizi Kestrel'in sunucu-taraflı istisna günlüğünden (`TypeError: Cannot read properties of null
(reading 'removeChild')`) doğrudan teşhis edildi.

## BannerZoneBuilder'a "Satırı/Kolonu Kopyala" Eklendi

Kullanıcı, ekran görüntüsünde bir Satır'ın buton çubuğundaki (Kolonlara Böl/Sil'in yanındaki) boş
alanı işaretleyip bu tür bir düğümü (tüm alt öğeleriyle) TEK tıkla kopyalayabilmek istedi.

**Uygulama:**
- Yeni `DuplicateBannerNodeCommand` (+ handler) bir Row ya da Column düğümünü ve TÜM alt ağacını
  (iç içe satırlar/kolonlar + onlara bağlı TÜM `BannerContent`ları) DERİN KOPYALAR. Kopya,
  orijinaliyle AYNI kardeş grubunda HEMEN YANINA yerleştirilir (`SortOrder = orijinal + 1`),
  ondan sonraki tüm kardeşler bir kaydırılır - kopya listenin SONUNA değil, kullanıcının
  BEKLEDİĞİ yere (orijinalin hemen ardına) düşer. `BaseEntity.Id`nin İSTEMCİ TARAFINDA (ctor'da)
  üretilmesi sayesinde `AddBannerRowCommand`daki İKİ AŞAMALI kaydetme kalıbı GEREKMEDİ - tüm alt
  ağaç bellekte (doğru Id/Path'lerle) kurulup TEK bir `SaveChanges` ile kaydediliyor.
- `BannerNodeEditor.razor`'a hem Row hem Column düğümleri için "Kopyala" butonu eklendi (buton
  çubuğunda "Kolonlara Böl"/"+İçine Satır Ekle" gibi mevcut aksiyonların yanında); yeni
  `OnDuplicateNode` EventCallback'i özyinelemeli bileşen çağrılarının hepsine iletildi.
  `BannerZoneBuilder.razor`'daki `DuplicateNodeAsync`, kopyalanan düğümü otomatik olarak SEÇİLİ hale
  getiriyor - kullanıcı hangi düğümün yeni kopya olduğunu hemen görüp düzenlemeye devam edebiliyor.
- Yeni GERÇEK bir entegrasyon testi (`BannerZoneDuplicationRegressionTests`, gerçek SQL Server'a
  karşı) eklendi - iç içe kolonlar/içerik/alt satır İÇEREN bir satırı kopyalayıp: (a) kopyanın
  kardeş sırasının DOĞRU olduğunu (orijinalin hemen yanı, sonraki kardeşin kaydığı), (b) tüm alt
  ağacın (kolonlar/içerik/iç içe satır) FARKLI Id'lerle ama AYNI alan değerleriyle klonlandığını,
  (c) orijinalin KENDİ alt ağacının bozulmadan kaldığını KANITLIYOR.

**Doğrulama:** `dotnet build` 0 hata, `dotnet test` **139/139** yeşil (yeni entegrasyon testi
dahil - gerçek SQL Server'a karşı çalışıp derin kopyalama mantığını uçtan uca doğruladı). Kestrel'de
canlı HTTP ile kullanıcının gerçek banner sayfasında "Kopyala" butonunun render edildiği doğrulandı.

**Sınırlama:** Butona gerçek bir tıklamayla sonucun tarayıcıda görsel olarak doğru göründüğünü
kanıtlamak yalnızca gerçek bir tarayıcıda mümkün - bu oturumda tarayıcı otomasyonu yok, ANCAK
komutun kendisi (asıl iş mantığı) yukarıdaki entegrasyon testiyle gerçek bir veritabanına karşı
uçtan uca doğrulandı.

## Sonraki fazlar (bkz. plan §13)

Faz 0/1, Faz 2, Faz 3, Faz 4'ün akış/stok/kampanya dilimleri ve Faz 8 (Muhasebe) TAMAMLANDI. Content
(CmsPage/Banner/Blog), RBAC yönetimi, çok para birimi (görüntüleme) ve Hangfire alt yapısı da bu
sürecin parçası olarak eklendi (bkz. yukarıdaki ilgili bölümlerin hepsi). Henüz yapılmadı / bilinçli
olarak kapsam dışı bırakıldı:

- Faz 2/3/8: Katalog, Storefront (görüntüleme+arama+SEO+çok dil+çok para birimi) ve Muhasebe
  taraflarında bilinen HİÇBİR orphaned entity kalmadı - bu oturum boyunca sistematik olarak tarandı
  ve kapatıldı (bkz. her bölümün kendi "Bulunan gerçek hata"/"Kapsam dışı" notları). Bu oturumda
  keşfedilen tüm orphaned alanlar (`CustomerGroup.ShowPricesOnStorefront`, `Cart.CustomerId` dahil)
  artık kapatıldı - bkz. ilgili bölümler.
- Faz 4-7: gerçek ödeme/kargo/pazaryeri API entegrasyonları (Provider Registry akışı UÇTAN UCA
  çalışıyor ve kanıtlanmış durumda - dinamik kimlik bilgisi ekranı dahil - ama 17 sağlayıcının hiçbiri
  gerçek bir dış API'ye bağlı değil, sandbox kimlik bilgisi olmadan güvenilir şekilde yazılıp test
  edilemez).
- Faz 9-10: `mobile/dekorras_app` Flutter projesi — bu ortamda Flutter SDK kurulu olmadığı için hiç
  başlatılmadı.
- Faz 11: Gerçek veri taşıma (KobiDirekt + BizimHesap → yeni şema) aracı - gerçek kaynak veri erişimi
  yok.
- `Domain.Shipping.ShippingMethod`/`ShippingRate`/`ShipmentTracking` (`ICargoProvider`'la çakışan,
  redundant tasarım kalıntısı) KALDIRILDI, `CustomsDeclaration` gerçek bir akışa bağlandı (bkz.
  aşağıdaki "Gümrük Beyanı" bölümü). `Setting` (key/value ayar deposu) ARTIK orphaned DEĞİL -
  `ContactSettingKeys` (İletişim Ayarları) ve `LayoutSettingKeys` (Site Genişliği Ayarları, bkz.
  aşağıdaki bölüm) tarafından gerçek kullanım senaryolarıyla kullanılıyor. Marketplace/
  `Customers.Affiliate` planın KENDİSİ tarafından "opsiyonel faz, MVP'ye zorunlu dahil etme" diye
  işaretlenmiş.
- `Dekorras.Api/Controllers/IntegrationsController.cs` hiçbir istemci (Admin/Storefront ikisi de
  kendi süreçlerinde doğrudan `ISender` çağırıyor) tarafından ÇAĞRILMIYOR - bu bir hata değil, henüz
  var olmayan bir mobil/3. parti istemci için hazırlanmış bir REST yüzeyi.
- Gerçek Testcontainers tabanlı entegrasyon testleri (şu an yerel SQLEXPRESS'e karşı çalışıyor).
