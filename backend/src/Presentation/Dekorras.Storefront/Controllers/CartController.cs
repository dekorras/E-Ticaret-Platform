using Dekorras.Application.Ordering.Storefront;
using Dekorras.Application.WallCovering;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class CartController(ISender sender) : Controller
{
    public async Task<IActionResult> Index()
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        var cart = await sender.Send(new GetCartQuery(sessionKey, StorefrontLanguage.GetLanguage(HttpContext)));
        return View(cart);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(Guid productId, int quantity, Guid? variantId, string? returnUrl)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        var customerId = await StorefrontCustomerId.ResolveAsync(sender, User);
        try
        {
            await sender.Send(new AddCartItemCommand(sessionKey, productId, quantity <= 0 ? 1 : quantity, variantId, customerGroupId, customerId));
        }
        catch (WallConfigurationRequiredException ex)
        {
            // Ölçüye özel ürün: ölçü seçtirmek için ürün sayfasındaki konfigüratöre gönderilir.
            return RedirectToAction("Details", "Product", new { slug = ex.ProductSlug });
        }

        return string.IsNullOrWhiteSpace(returnUrl) ? RedirectToAction(nameof(Index)) : LocalRedirect(returnUrl);
    }

    /// <summary>Konfigüratör formunun JS'siz gönderimi (progressive enhancement). JS varsa form
    /// /api/v1/cart/items'a gider; burada da aynı komut çalışır, fiyat sunucuda hesaplanır.
    /// Hata olursa ürün sayfasına girilen konfigürasyonla (URL parametreleri) geri dönülür.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddConfigured(Guid productId, string slug, int quantity, [FromForm] WallConfigurationInput input)
    {
        var materials = await sender.Send(new GetMaterialsQuery(productId));
        var settings = await sender.Send(new GetWallCoveringSettingsQuery());
        var configuration = input.Parse(materials.FirstOrDefault()?.Code ?? "", settings.DefaultWidthCm, settings.DefaultHeightCm);

        try
        {
            var customerId = await StorefrontCustomerId.ResolveAsync(sender, User);
            await sender.Send(new AddConfiguredCartItemCommand(CartSession.GetOrCreateSessionKey(HttpContext), productId, configuration, Math.Clamp(quantity, 1, 99), customerId));
            return RedirectToAction(nameof(Index));
        }
        catch (WallConfigurationException ex)
        {
            TempData["WallError"] = ex.Message;
            var query = string.Join("&", WallConfigurationInput.ToQuery(configuration).Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            return Redirect($"{Url.Action("Details", "Product", new { slug })}&{query}");
        }
    }

    /// <summary>"Hemen Al" (bkz. plan §2.2 - "Sepete Ekle" / "Hemen Al") - ürünü sepete ekleyip
    /// müşteriyi sepet sayfasını hiç göstermeden DOĞRUDAN checkout'a yönlendirir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuyNow(Guid productId, int quantity, Guid? variantId)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        var customerId = await StorefrontCustomerId.ResolveAsync(sender, User);
        try
        {
            await sender.Send(new AddCartItemCommand(sessionKey, productId, quantity <= 0 ? 1 : quantity, variantId, customerGroupId, customerId));
        }
        catch (WallConfigurationRequiredException ex)
        {
            // Ölçüye özel ürün: ölçü seçtirmek için ürün sayfasındaki konfigüratöre gönderilir.
            return RedirectToAction("Details", "Product", new { slug = ex.ProductSlug });
        }

        return RedirectToAction("Index", "Checkout");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(Guid productId, int quantity, Guid? variantId, Guid? cartItemId)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);

        // Ölçüye özel satırlar aynı ürünün birden çok konfigürasyonunu taşıyabildiği için satır kimliğiyle yönetilir.
        if (cartItemId is Guid itemId)
        {
            try
            {
                if (quantity > 0) await sender.Send(new UpdateCartItemQuantityByIdCommand(sessionKey, itemId, Math.Min(quantity, 99)));
                else await sender.Send(new RemoveCartItemByIdCommand(sessionKey, itemId));
            }
            catch (Exception ex) when (ex is WallConfigurationException or KeyNotFoundException)
            {
                TempData["CouponMessage"] = ex.Message;
                TempData["CouponSuccess"] = false;
            }
            return RedirectToAction(nameof(Index));
        }

        if (quantity > 0)
        {
            var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
            await sender.Send(new UpdateCartItemQuantityCommand(sessionKey, productId, quantity, variantId, customerGroupId));
        }
        else
        {
            await sender.Send(new RemoveCartItemCommand(sessionKey, productId, variantId));
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid productId, Guid? variantId, Guid? cartItemId)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        if (cartItemId is Guid itemId) await sender.Send(new RemoveCartItemByIdCommand(sessionKey, itemId));
        else await sender.Send(new RemoveCartItemCommand(sessionKey, productId, variantId));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon(string couponCode)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        var result = await sender.Send(new ApplyCouponCommand(sessionKey, couponCode));
        TempData["CouponMessage"] = result.Message;
        TempData["CouponSuccess"] = result.Success;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCoupon()
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        await sender.Send(new RemoveCouponCommand(sessionKey));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyGiftVoucher(string giftVoucherCode)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        var result = await sender.Send(new ApplyGiftVoucherCommand(sessionKey, giftVoucherCode));
        TempData["GiftVoucherMessage"] = result.Message;
        TempData["GiftVoucherSuccess"] = result.Success;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveGiftVoucher()
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        await sender.Send(new RemoveGiftVoucherCommand(sessionKey));
        return RedirectToAction(nameof(Index));
    }
}
