using Dekorras.Application.Ordering.Storefront;
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
        await sender.Send(new AddCartItemCommand(sessionKey, productId, quantity <= 0 ? 1 : quantity, variantId, customerGroupId, customerId));

        return string.IsNullOrWhiteSpace(returnUrl) ? RedirectToAction(nameof(Index)) : LocalRedirect(returnUrl);
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
        await sender.Send(new AddCartItemCommand(sessionKey, productId, quantity <= 0 ? 1 : quantity, variantId, customerGroupId, customerId));

        return RedirectToAction("Index", "Checkout");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(Guid productId, int quantity, Guid? variantId)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
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
    public async Task<IActionResult> Remove(Guid productId, Guid? variantId)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        await sender.Send(new RemoveCartItemCommand(sessionKey, productId, variantId));
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
