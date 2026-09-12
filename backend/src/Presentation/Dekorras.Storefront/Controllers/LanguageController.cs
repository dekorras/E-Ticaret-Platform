using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class LanguageController : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string code, string? returnUrl)
    {
        StorefrontLanguage.SetLanguage(HttpContext, code);
        return string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)
            ? RedirectToAction("Index", "Home")
            : LocalRedirect(returnUrl);
    }
}
