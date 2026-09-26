using Dekorras.Application.WallCovering;
using Dekorras.Storefront.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Dekorras.Storefront.Controllers;

/// <summary>Poster/duvar kağıdı kataloğu (spec 1.6.1). Filtre + sayfa durumu URL'de tutulur; JS varsa
/// sonuçlar `/duvar-kagitlari/_liste` parçasıyla tam sayfa yenilemeden güncellenir, yoksa normal
/// GET formu + `?page=` bağlantılarıyla çalışır (SEO). Kişisel durum (favori/deneme listesi) karta
/// render EDİLMEZ, JS ile işaretlenir - bu sayede liste parçası çıktı önbelleğine alınabilir.</summary>
[Route("duvar-kagitlari")]
public class WallpaperController(ISender sender) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] WallCatalogRequest request)
    {
        var result = await sender.Send(request.ToQuery());
        var materials = await sender.Send(new GetMaterialsQuery());
        var categories = await sender.Send(new GetWallCategoriesQuery());
        return View(new WallCatalogPageModel(request, result, materials.Select(m => m.PricePerM2).DefaultIfEmpty(0m).Min()) { Categories = categories.Categories });
    }

    [HttpGet("_liste")]
    [OutputCache(Duration = 60, VaryByQueryKeys = ["*"])]
    public async Task<IActionResult> List([FromQuery] WallCatalogRequest request)
    {
        var result = await sender.Send(request.ToQuery());
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();
        Response.Headers["X-Total-Pages"] = result.TotalPages.ToString();
        return PartialView("_WallGrid", result);
    }

    [HttpGet("/duvarimda-dene/{token}")]
    public async Task<IActionResult> Shared(string token)
    {
        var list = await sender.Send(new GetSharedTryOnListQuery(token));
        if (list is null) return NotFound();
        return View(list);
    }
}
