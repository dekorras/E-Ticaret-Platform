using Dekorras.Application.Customers.Commands;
using Dekorras.Application.Customers.Queries;
using Dekorras.Application.Ordering.Queries;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Storefront.Models;
using Dekorras.Storefront.WallCovering;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dekorras.Storefront.Controllers;

/// <summary>
/// Storefront'un kendi müşteri hesapları - Admin panelinin yönetici girişinden TAMAMEN AYRIDIR
/// (aynı AspNetUsers tablosunu paylaşırlar ama bir müşterinin hiçbir AdminProfile/Role/Permission
/// kaydı olmaz, dolayısıyla Admin panelindeki RBAC policy'lerinden hiçbirini karşılamaz).
/// Storefront geleneksel MVC olduğu için (Blazor Server değil), Admin'deki gibi "düz HTML form +
/// minimal API" atlatmasına gerek yoktur - her istek zaten kendi başına bir HTTP request/response
/// döngüsüdür, bu yüzden UserManager/SignInManager doğrudan controller action'ları içinde kullanılabilir.
/// </summary>
public class AccountController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, ISender sender) : Controller
{
    public IActionResult Register() => View(new RegisterFormModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterFormModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new IdentityUser { UserName = model.Email, Email = model.Email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var customerId = await sender.Send(new CreateCustomerProfileCommand(user.Id, model.FullName, model.Email));
        await signInManager.SignInAsync(user, isPersistent: true);

        // Bkz. MergeGuestCartIntoCustomerCommand - kayıt sırasında elde bir misafir sepeti varsa
        // (bkz. CartSession) yeni müşteriye bağlanır, aksi halde sepet başka bir cihazdan asla
        // erişilemez olurdu (`Cart.CustomerId` Faz 0/1'den beri hiç set edilmiyordu).
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        await sender.Send(new MergeGuestCartIntoCustomerCommand(sessionKey, customerId));
        await WallVisitor.MergeGuestDataAsync(HttpContext, sender, user.Id, customerId);

        return RedirectToAction("Index", "Home");
    }

    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginFormModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(model.Email, model.Password, isPersistent: true, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Geçersiz e-posta veya şifre.");
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        // Bkz. Register'daki aynı not - giriş yapan müşterinin bu cihazdaki (varsa) misafir sepeti
        // müşteriye bağlanır VE müşterinin başka bir cihazda kayıtlı sepeti varsa bu cihaza taşınır.
        // Not: PasswordSignInAsync çerezi YANITA yazar; bu istekte `User` hâlâ anonimdir - kimlik bu
        // yüzden `GetUserId(User)` ile değil e-postadan çözülür (eskiden null dönüyor, birleştirme hiç çalışmıyordu).
        var signedInUser = await userManager.FindByEmailAsync(model.Email);
        var identityUserId = signedInUser?.Id;
        var customerId = await sender.Send(new GetMyCustomerIdQuery(identityUserId));
        if (customerId is Guid resolvedCustomerId)
        {
            var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
            await sender.Send(new MergeGuestCartIntoCustomerCommand(sessionKey, resolvedCustomerId));
            await WallVisitor.MergeGuestDataAsync(HttpContext, sender, identityUserId!, resolvedCustomerId);
        }

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        // Bkz. CartSession.ClearSessionKey - aksi halde paylaşılan bir cihazda bir sonraki kullanıcı
        // bu müşterinin CustomerId'ye bağlı sepetini devralabilirdi.
        CartSession.ClearSessionKey(HttpContext);
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    public IActionResult Index() => RedirectToAction(nameof(Orders));

    /// <summary>Siparişlerim: arama (sipariş no / ürün adı), durum filtresi (Tümü, Devam edenler, İptaller, İadeler,
    /// Teslim edilemeyenler) ve dönem (Tüm siparişler / son 30-90-180 gün / yıl).</summary>
    [Authorize]
    public async Task<IActionResult> Orders(string? q, MyOrderFilter filter = MyOrderFilter.All, string? period = null)
    {
        var identityUserId = userManager.GetUserId(User)!;
        var page = await sender.Send(new GetMyOrdersPageQuery(identityUserId, q, filter, period));
        ViewBag.Search = q;
        ViewBag.Filter = filter;
        ViewBag.Period = period;
        return View(page);
    }

    [Authorize]
    public async Task<IActionResult> Offers() =>
        View(await sender.Send(new GetMyOffersQuery(userManager.GetUserId(User)!)));

    [Authorize]
    public async Task<IActionResult> Requests() =>
        View(await sender.Send(new GetMyRequestsQuery(userManager.GetUserId(User)!)));

    [Authorize]
    public async Task<IActionResult> Reviews() =>
        View(await sender.Send(new GetMyReviewsQuery(userManager.GetUserId(User)!)));

    [Authorize]
    public async Task<IActionResult> Coupons() =>
        View(await sender.Send(new GetMyCouponsQuery(userManager.GetUserId(User)!)));

    /// <summary>Kullanıcı bilgilerim: ad-soyad/telefon, şifre değiştirme, bülten; adresler de bu başlığın altında.</summary>
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var profile = await sender.Send(new GetMyCustomerProfileQuery(userManager.GetUserId(User)!));
        return View(new ProfileFormModel
        {
            FullName = profile?.FullName ?? "",
            Email = profile?.Email ?? User.Identity?.Name ?? "",
            PhoneNumber = profile?.PhoneNumber,
            NewsletterSubscribed = profile?.NewsletterSubscribed ?? false
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileFormModel model)
    {
        ModelState.Remove(nameof(ProfileFormModel.Email));
        if (!ModelState.IsValid) return View(model);
        try
        {
            await sender.Send(new UpdateMyContactCommand(userManager.GetUserId(User)!, model.FullName, model.PhoneNumber));
        }
        catch (FluentValidation.ValidationException ex)
        {
            foreach (var e in ex.Errors) ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
            return View(model);
        }
        TempData["ProfileMessage"] = "Bilgileriniz güncellendi.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword)
        {
            TempData["PasswordError"] = "Yeni şifre ile tekrarı aynı olmalıdır.";
            return RedirectToAction(nameof(Profile));
        }
        var result = await userManager.ChangePasswordAsync(user, currentPassword ?? "", newPassword);
        if (!result.Succeeded)
        {
            TempData["PasswordError"] = string.Join(" ", result.Errors.Select(e => e.Code == "PasswordMismatch" ? "Mevcut şifre hatalı." : e.Description));
            return RedirectToAction(nameof(Profile));
        }
        await signInManager.RefreshSignInAsync(user);
        TempData["PasswordMessage"] = "Şifreniz değiştirildi.";
        return RedirectToAction(nameof(Profile));
    }

    /// <summary>Tüm listelerim: Beğendiklerim + "Duvarımda Dene" listesi.</summary>
    [Authorize]
    public async Task<IActionResult> Lists()
    {
        var identityUserId = userManager.GetUserId(User)!;
        ViewBag.WishlistCount = (await sender.Send(new GetMyWishlistQuery(identityUserId, StorefrontLanguage.GetLanguage(HttpContext)))).Count;
        var tryOn = await sender.Send(new Dekorras.Application.WallCovering.GetTryOnListQuery(await WallVisitor.GetOwnerKeyAsync(HttpContext, sender)));
        return View(tryOn);
    }

    [Authorize]
    public async Task<IActionResult> CustomerService()
    {
        var settings = await sender.Send(new Dekorras.Application.SystemAdmin.Queries.GetSettingsQuery(Dekorras.Application.Common.ContactSettingKeys.All));
        return View(settings.ToDictionary(s => s.Key, s => s.Value));
    }

    [Authorize]
    public async Task<IActionResult> OrderDetail(Guid id)
    {
        // Sipariş sahipliği kontrolü: GetOrderByIdQuery herhangi bir ID için detay döner (Admin
        // panelinde bilinçli olarak böyle - yönetici tüm siparişleri görebilir), bu yüzden burada
        // müşterinin KENDİ sipariş listesinde olduğunu ayrıca doğruluyoruz - aksi halde bir müşteri
        // başka birinin sipariş GUID'ini tahmin ederek onun bilgilerini görebilirdi.
        var identityUserId = userManager.GetUserId(User)!;
        var myOrders = await sender.Send(new GetMyOrdersQuery(identityUserId));
        if (myOrders.All(o => o.Id != id))
            return NotFound();

        var order = await sender.Send(new GetOrderByIdQuery(id));
        if (order is null) return NotFound();
        ViewBag.WallItems = await sender.Send(new Dekorras.Application.WallCovering.GetOrderWallItemsQuery(id));
        return View(order);
    }

    [Authorize]
    public async Task<IActionResult> Wishlist()
    {
        var identityUserId = userManager.GetUserId(User)!;
        var items = await sender.Send(new GetMyWishlistQuery(identityUserId, StorefrontLanguage.GetLanguage(HttpContext)));
        var profile = await sender.Send(new GetMyCustomerProfileQuery(identityUserId));

        ViewBag.NewsletterSubscribed = profile?.NewsletterSubscribed ?? false;
        return View(items);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToWishlist(Guid productId, string? returnUrl)
    {
        var identityUserId = userManager.GetUserId(User)!;
        await sender.Send(new AddToWishlistCommand(identityUserId, productId));

        return string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)
            ? RedirectToAction(nameof(Wishlist))
            : LocalRedirect(returnUrl);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveFromWishlist(Guid productId, string? returnUrl)
    {
        var identityUserId = userManager.GetUserId(User)!;
        await sender.Send(new RemoveFromWishlistCommand(identityUserId, productId));

        return string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)
            ? RedirectToAction(nameof(Wishlist))
            : LocalRedirect(returnUrl);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetNewsletterSubscription(bool subscribed)
    {
        var identityUserId = userManager.GetUserId(User)!;
        await sender.Send(new SetNewsletterSubscriptionCommand(identityUserId, subscribed));
        TempData["ProfileMessage"] = subscribed ? "Bülten aboneliğiniz açıldı." : "Bülten aboneliğiniz kapatıldı.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    public async Task<IActionResult> Addresses()
    {
        var identityUserId = userManager.GetUserId(User)!;
        var profile = await sender.Send(new GetMyCustomerProfileQuery(identityUserId));
        return View(profile?.Addresses ?? []);
    }

    [Authorize]
    public IActionResult AddAddress() => View(new AddressFormModel());

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAddress(AddressFormModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var identityUserId = userManager.GetUserId(User)!;
        await sender.Send(new AddCustomerAddressCommand(
            identityUserId, model.RecipientName, model.CountryCode, model.City, model.AddressLine1,
            model.PhoneNumber, model.State, model.District, model.PostalCode, model.AddressLine2));

        return RedirectToAction(nameof(Addresses));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAddress(Guid addressId)
    {
        var identityUserId = userManager.GetUserId(User)!;
        // Sahiplik ihlali/olmayan ID denemesi ham 500 yerine sessizce yok sayılır - müşteri her
        // durumda kendi adres listesine geri döner.
        try { await sender.Send(new RemoveCustomerAddressCommand(addressId, identityUserId)); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { }
        return RedirectToAction(nameof(Addresses));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultShippingAddress(Guid addressId)
    {
        var identityUserId = userManager.GetUserId(User)!;
        try { await sender.Send(new SetDefaultShippingAddressCommand(addressId, identityUserId)); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { }
        return RedirectToAction(nameof(Addresses));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultBillingAddress(Guid addressId)
    {
        var identityUserId = userManager.GetUserId(User)!;
        try { await sender.Send(new SetDefaultBillingAddressCommand(addressId, identityUserId)); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { }
        return RedirectToAction(nameof(Addresses));
    }
}
