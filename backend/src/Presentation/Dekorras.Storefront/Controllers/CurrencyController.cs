using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class CurrencyController : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetCurrency(string code, string? returnUrl)
    {
        StorefrontCurrency.SetCurrencyCode(HttpContext, code);
        return string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)
            ? RedirectToAction("Index", "Home")
            : LocalRedirect(returnUrl);
    }
}
