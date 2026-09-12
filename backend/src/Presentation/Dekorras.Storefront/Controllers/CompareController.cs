using Dekorras.Application.Catalog.Storefront;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class CompareController(ISender sender) : Controller
{
    public async Task<IActionResult> Index()
    {
        var productIds = StorefrontCompareList.GetProductIds(HttpContext);
        var products = await sender.Send(new GetProductsForComparisonQuery(productIds, StorefrontLanguage.GetLanguage(HttpContext)));
        return View(products);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Add(Guid productId, string? returnUrl)
    {
        StorefrontCompareList.AddProduct(HttpContext, productId);
        return string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? RedirectToAction(nameof(Index)) : LocalRedirect(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(Guid productId, string? returnUrl)
    {
        StorefrontCompareList.RemoveProduct(HttpContext, productId);
        return string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? RedirectToAction(nameof(Index)) : LocalRedirect(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Clear()
    {
        StorefrontCompareList.Clear(HttpContext);
        return RedirectToAction(nameof(Index));
    }
}
