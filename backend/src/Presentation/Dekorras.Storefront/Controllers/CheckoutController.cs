using Dekorras.Application.Customers.Queries;
using Dekorras.Application.Integrations.Queries;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Domain.Integrations;
using Dekorras.Storefront.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class CheckoutController(ISender sender, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);
        var cart = await sender.Send(new GetCartQuery(sessionKey, StorefrontLanguage.GetLanguage(HttpContext)));
        if (cart.Items.Count == 0)
            return RedirectToAction("Index", "Cart");

        await PopulateViewBagAsync(cart);

        var model = new CheckoutFormModel();
        if (User.Identity?.IsAuthenticated == true)
        {
            var profile = await sender.Send(new GetMyCustomerProfileQuery(userManager.GetUserId(User)!));
            if (profile is not null)
            {
                model.FullName = profile.FullName;
                model.Email = profile.Email;
                model.PhoneNumber = profile.PhoneNumber ?? "";
            }
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(CheckoutFormModel model)
    {
        var sessionKey = CartSession.GetOrCreateSessionKey(HttpContext);

        if (!ModelState.IsValid)
        {
            var cart = await sender.Send(new GetCartQuery(sessionKey, StorefrontLanguage.GetLanguage(HttpContext)));
            await PopulateViewBagAsync(cart);
            return View("Index", model);
        }

        var identityUserId = User.Identity?.IsAuthenticated == true ? userManager.GetUserId(User) : null;

        try
        {
            var result = await sender.Send(new PlaceOrderCommand(
                sessionKey, identityUserId, model.FullName, model.Email, model.PhoneNumber,
                model.CountryCode, model.City, model.AddressLine1, model.PaymentProviderKey, model.CargoProviderKey));

            return RedirectToAction(nameof(Confirmation), new { orderNumber = result.OrderNumber });
        }
        catch (InvalidOperationException ex)
        {
            // Stok yetersizliği/sağlayıcı hataları gibi beklenen iş kuralı ihlalleri - ham 500
            // sayfası yerine checkout formuna geri dönüp mesajı göster (sepet/stok DEĞİŞMEDİ,
            // PlaceOrderCommand hatada hiçbir şey kaydetmeden atıyor).
            ModelState.AddModelError(string.Empty, ex.Message);
            var cart = await sender.Send(new GetCartQuery(sessionKey, StorefrontLanguage.GetLanguage(HttpContext)));
            await PopulateViewBagAsync(cart);
            return View("Index", model);
        }
    }

    public IActionResult Confirmation(string orderNumber)
    {
        ViewBag.OrderNumber = orderNumber;
        return View();
    }

    private async Task PopulateViewBagAsync(CartDto cart)
    {
        ViewBag.Cart = cart;
        ViewBag.PaymentProviders = await sender.Send(new GetActiveProvidersQuery(ProviderCategory.Payment));
        ViewBag.CargoProviders = await sender.Send(new GetActiveProvidersQuery(ProviderCategory.Cargo));
    }
}
