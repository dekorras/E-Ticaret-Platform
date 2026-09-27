# "Hesabım" Müşteri Alanı (Pazaryeri Tarzı) — Geliştirme Promptu

> Aşağıdaki metni bir kod üreten yapay zekâya olduğu gibi verebilirsiniz. `[KÖŞELİ PARANTEZ]` içindeki yerleri kendi projenize göre doldurun. Referans düzen: Hepsiburada "Hesabım → Siparişlerim" ekranı.

* * *

## 1\. Rol ve Bağlam

Sen kıdemli bir **ASP.NET Core** geliştiricisisin; Onion mimari, MediatR (CQRS), FluentValidation, EF Core ve Bootstrap 5.3 konularında uzmansın.

Mevcut projem:

- **Framework:** ASP.NET Core `[10]` — Storefront **MVC (Razor)**, Admin **Blazor Server**
- **Mimari:** Onion — `Domain / Application / Infrastructure / Persistence / Presentation`
- **Uygulama katmanı:** MediatR sorgu/komutları, FluentValidation doğrulayıcıları, `IUnitOfWork.Repository<T>().Query()`
- **Kimlik:** ASP.NET Core Identity; müşteri kaydı `Customer.IdentityUserId` ile kullanıcıya bağlı
- **Ön yüz:** Bootstrap 5.3.3 (+ `bootstrap.bundle.min.js`), Bootstrap Icons, vanilla JavaScript
- **Veritabanı:** Microsoft SQL Server (Docker **kullanılmaz**)
- **Proje adı / namespace:** `[Dekorras]`

## 2\. Amaç

Müşterinin "Hesabım" alanını, büyük pazaryerlerindeki gibi **solda sabit bir menü, sağda seçili bölümün içeriği** olacak şekilde yeniden kur. Menü başlıkları **birebir** aşağıdaki gibi olmalı ve her bilgi kendi başlığının altında tutulmalı:

| Sıra | Menü başlığı | Rota | İçerik |
| --- | --- | --- | --- |
| 1 | Siparişlerim | `/Account/Orders` | Arama, durum filtreleri, dönem seçimi, açılır sipariş kartları |
| 2 | Sana Özel Fırsatlar | `/Account/Offers` | Geçerli kampanyalar + müşteri grubuna özel fiyatlar |
| 3 | Soru ve Taleplerim | `/Account/Requests` | Ürün soruları (cevaplarıyla) + tasarım/özel ölçü talepleri |
| 4 | Değerlendirmelerim | `/Account/Reviews` | Yapılan değerlendirmeler + değerlendirme bekleyen ürünler |
| 5 | Kuponlarım | `/Account/Coupons` | Siparişlerde kullanılan kuponlar |
| 6 | Kullanıcı bilgilerim | `/Account/Profile` | Ad-soyad/telefon, şifre değişikliği, adresler, bülten |
| — | **LİSTELERİM** (grup başlığı) | — | — |
| 7 | Beğendiklerim | `/Account/Wishlist` | Favori ürünler |
| 8 | Tüm listelerim | `/Account/Lists` | Beğendiklerim + "Duvarımda Dene" listesi özet kartları |
| 9 | Müşteri Hizmetleri | `/Account/CustomerService` | Telefon, WhatsApp, sosyal medya, iletişim sayfası |
| 10 | Çıkış yap | `POST /Account/Logout` | Form + antiforgery |

Kurallar:

- Referanstaki **Hepsipay** başlığı **konmayacak**.
- Referanstaki "Premium" kutusunun yerine **"DUVARIMDA DENE — Ürünleri kendi odanızda görün ›"** tanıtım kutusu olacak (`/duvar-kagitlari`'na gider).
- Menünün üstünde müşterinin **baş harfleri (avatar)** ve **adı soyadı** görünür.
- `/Account` → `/Account/Orders`'a yönlendirir. Adreslerim, Yeni Adres ve Sipariş Detayı sayfaları da aynı düzeni kullanır (etkin başlık: sırasıyla "Kullanıcı bilgilerim", "Kullanıcı bilgilerim", "Siparişlerim").
- Üst menüdeki "Hesabım" açılır menüsü aynı başlıkları aynı sırayla listeler.

Kodu yazarken hiçbir adımı "burayı sen tamamlarsın" diye boş bırakma; çalışır, uçtan uca bir çözüm üret.

* * *

## 3\. Uygulama Katmanı (MediatR)

Tek dosyada topla: `Application/Customers/Queries/MyAccountQueries.cs`. Her sorgu **yalnızca oturumdaki kullanıcının** verisini döndürür (`IdentityUserId` ile müşteri bulunur; müşteri yoksa boş sonuç döner, hata fırlatmaz).

### 3\.1 Siparişlerim

```csharp
public enum MyOrderFilter { All, Ongoing, Cancelled, Returned, Undelivered }
public sealed record GetMyOrdersPageQuery(string IdentityUserId, string? Search = null,
    MyOrderFilter Filter = MyOrderFilter.All, string? Period = null) : IRequest<MyOrdersPageDto>;
public sealed record MyOrdersPageDto(IReadOnlyList<MyOrderCardDto> Orders, IReadOnlyList<int> Years, int TotalOrderCount);
```

Durum grupları (`OrderStatus` → filtre):

| Filtre | Etiket | Durumlar |
| --- | --- | --- |
| `Ongoing` | Devam edenler | PendingApproval, Preparing, Prepared, Shipped, CancellationReverted, OnHold |
| `Cancelled` | İptaller | Cancelled, Voided |
| `Returned` | İadeler | Refunded, Chargeback |
| `Undelivered` | Teslim edilemeyenler | Rejected, Expired, Failed |

- **Arama:** sipariş numarası **veya** sipariş kalemindeki ürün adı içinde geçen metin.
- **Dönem:** `"30"`, `"90"`, `"180"` (son N gün) ya da yıl (`"2026"`). `Years` = müşterinin sipariş verdiği yıllar (azalan).
- **Kart verisi:** sipariş no, durum, tarih, genel toplam, kargo takip no, kalemler (ad, adet, satır toplamı, ürün slug'ı, küçük görsel).
- `TotalOrderCount`, filtreden bağımsız toplam sipariş sayısıdır ("Henüz siparişiniz yok" ile "Bu seçime uyan sipariş yok" ayrımı için).

### 3\.2 Diğer Sorgular / Komut

| Sorgu / Komut | Dönüş | Kural |
| --- | --- | --- |
| `GetMyRequestsQuery` | `MyRequestsDto(Questions, DesignRequests)` | Tasarım talepleri e-posta ile eşleşir (**büyük/küçük harf duyarsız**) |
| `GetMyReviewsQuery` | `MyReviewsDto(Reviews, Pending)` | `Pending` = **tamamlanmış** siparişlerdeki henüz değerlendirilmemiş ürünler (iptal/iade hariç) |
| `GetMyCouponsQuery` | `IReadOnlyList<MyCouponUsageDto>` | Yalnızca müşterinin siparişlerinde **kullandığı** kuponlar; hâlâ geçerli mi, indirim metni (`%10 indirim`) |
| `GetMyOffersQuery` | `MyOffersDto(CustomerGroupName, Campaigns, GroupPrices)` | Yalnızca şu an geçerli kampanyalar; grup fiyatı yalnızca taban fiyattan **düşükse** listelenir |
| `UpdateMyContactCommand(IdentityUserId, FullName, PhoneNumber)` | — | Validator: ad zorunlu (≤200), telefon ≤30 ve yalnızca rakam/boşluk/`+()-`; ad kırpılarak kaydedilir |

> **Güvenlik:** Kullanılabilir tüm kupon kodlarını listeleme — kod sızdırır. E-posta adresi profil sayfasında **salt okunur**dur (giriş bilgisidir).

* * *

## 4\. Sunum Katmanı (MVC)

- **`AccountController`** (`[Authorize]`): Yukarıdaki her rota için action; `Profile` GET/POST, `ChangePassword` POST (Identity `ChangePasswordAsync` + `RefreshSignInAsync`; yanlış mevcut şifrede "Mevcut şifre hatalı."). Mesajlar `TempData` ile (PRG deseni). Tüm POST'larda `[ValidateAntiForgeryToken]`.
- **`AccountSidebarViewComponent`**: `Model(FullName, Initials, Active)` — etkin başlık sayfanın `ViewData["AccountNav"]` değerinden gelir (`orders`, `offers`, `requests`, `reviews`, `coupons`, `profile`, `wishlist`, `lists`, `support`).
- **`Views/Account/_AccountLayout.cshtml`**: `_Layout`'u kullanır; `row g-4` içinde `col-lg-3` menü + `col-lg-9` içerik; alt sayfaların `Styles`/`Scripts` bölümlerini aktarır.

* * *

## 5\. Ön Yüz — Bootstrap Kuralları (ZORUNLU)

Düzen **tamamen Bootstrap 5.3 bileşen ve yardımcı sınıflarıyla** kurulmalı; özel CSS dosyası (`account.css`) **yüklenmese bile** sayfa düzgün görünmelidir. Özel CSS yalnızca rötuş içindir (hover rengi, açılınca dönen ok).

- **Marka rengi:** `:root` içinde `--bs-primary`, `--bs-primary-rgb`, `--bs-primary-bg-subtle`, `--bs-primary-border-subtle`, `--bs-primary-text-emphasis` marka turuncusuna (`#E96631`) eşitlenir; böylece `text-primary`, `bg-primary-subtle` vb. uyumlu olur.
- **Sol menü:** `list-group list-group-flush`; öğeler `list-group-item list-group-item-action border-0 d-flex align-items-center gap-3 rounded`; etkin öğe `text-primary fw-semibold`. "LİSTELERİM" `small fw-bold text-uppercase text-body-secondary` grup başlığı, altındaki iki öğe `ps-4` ile içeriden. Avatar `rounded-circle bg-body-secondary` (56×56). Tanıtım kutusu `bg-danger-subtle rounded-3 p-3`, başlık ve açıklama **alt alta**.
- **Siparişlerim araç çubuğu:** `row g-2` — `input-group input-group-sm` arama; durum filtreleri `btn btn-sm` (seçili: `btn-primary`, diğerleri `btn-outline-secondary`; `btn-outline-primary active` **kullanma**, Bootstrap'in derlenmiş mavisi çıkar); dönem `form-select form-select-sm` (değişince formu gönderir, `<noscript>` "Uygula" düğmesi).
- **Sipariş kartı:** `card shadow-sm`; başlık `card-header bg-body` + `data-bs-toggle="collapse"`; içinde küçük görseller (44×44, `object-fit-cover`), "Sipariş no", durum ikonu (`text-success` tamamlandı, `text-warning` devam, `text-danger` iptal/teslim edilemedi, `text-info` iade), tarih (`dd MMMM yyyy`, tr-TR) ve yeşil toplam (`N2 TL`), sağda `bi-chevron-down`. Gövde `collapse` + `list-group list-group-flush` kalemler + `card-footer` "Sipariş detayı". Başlık klavyeyle (Enter/Boşluk) de açılmalı.
- **Diğer bölümler:** her blok `card card-body shadow-sm mb-3`, blok başlığı `h2.h6.fw-bold`; satırlar `d-flex gap-3 py-3 border-top`; görseller 56×56 `rounded border object-fit-cover`.
- **Sayfa başlığı:** `h1.h4.fw-bold.mb-3` ve metni menü başlığıyla **birebir aynı**.
- **Mobil:** `col-lg-*` kırılımı; sipariş kartında görseller + ok üstte, sipariş no tam genişlik, durum/tarih altta iki kolon.

* * *

## 6\. Testler

- **Entegrasyon (gerçek SQL Server, geçici veritabanı):** iki müşteri + farklı durumlarda siparişler kur; her filtrenin, aramanın (sipariş no / ürün adı), dönemin, başka müşterinin siparişinin görünmediğinin; soru/talep eşleşmesinin; bekleyen değerlendirmenin değerlendirince kaybolduğunun; kupon ve fırsat listelerinin; profil güncelleme ve doğrulama hatalarının testi.
- **E2E (Playwright, sistemdeki Edge — tarayıcı indirilmez):** yeni üye ol → soldaki her başlığa tıkla → `h1` metni ve etkin menü öğesi başlıkla aynı mı → sipariş kartı açılıyor mu → **`account.css` engellenmişken** menü hâlâ alt alta ve sipariş hâlâ kart mı. Eklenen geçici müşteri/sipariş/kullanıcı test sonunda silinir.

## 7\. Teslim Edilecekler

1. `MyAccountQueries.cs` (sorgular, DTO'lar, komut + validator)
2. `AccountController` action'ları ve `ProfileFormModel`
3. `AccountSidebarViewComponent` + `Default.cshtml`, `_AccountLayout.cshtml`
4. Görünümler: `Orders`, `Offers`, `Requests`, `Reviews`, `Coupons`, `Profile`, `Lists`, `CustomerService` (+ `Wishlist`, `Addresses`, `AddAddress`, `OrderDetail` yeni düzene geçer)
5. `account.css` (yalnız rötuş) ve `variables.css`'e Bootstrap primary eşlemesi
6. `_Layout.cshtml` "Hesabım" açılır menüsü güncellemesi
7. Entegrasyon + E2E testleri

## 8\. Kalite Kriterleri

- Derleme **0 hata / 0 uyarı**; mevcut tüm testler yeşil kalır.
- Hiçbir sorgu başka müşterinin verisini döndürmez; tüm POST'lar antiforgery korumalı.
- Türkçe metinler ve tarih/para biçimi `tr-TR`.
- Özel CSS kaldırıldığında da düzen bozulmaz (E2E ile kanıtlanır).
- Gizli bilgi içeren `appsettings.json` commit'lenmez.
