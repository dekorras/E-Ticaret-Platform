using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using Dekorras.Storefront.Models;
using Dekorras.Storefront.WallCovering;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

/// <summary>"Duvarında Gör" görüntüleyicisi (spec 1.6.3 / 1.6.6-B). Deep link sözleşmesi:
/// /duvarinda-gor?product=&amp;scene=&amp;w_cm=&amp;h_cm=&amp;unit=&amp;material=&amp;fit=&amp;mirror=&amp;filter=&amp;crop=&amp;align=&amp;return=
/// ve kısa yol /p/{slug}/duvarinda-gor. Geçersiz parametreler sessizce varsayılana düşer; geçersiz/pasif
/// ürün çıplak 404 yerine katalog önerileriyle "Ürün bulunamadı" sayfası gösterir (HTTP durumu yine 404).</summary>
public class WallPreviewController(ISender sender) : Controller
{
    [HttpGet("/duvarinda-gor")]
    public Task<IActionResult> Index(string? product, string? scene, string? align, string? @return, string? modal, string? list) =>
        RenderAsync(product, scene, align, @return, modal is "1" or "true" ? "modal" : "page", list is "1");

    [HttpGet("/p/{slug}/duvarinda-gor")]
    public Task<IActionResult> Short(string slug, string? scene, string? align, string? @return, string? modal) =>
        RenderAsync(slug, scene, align, @return, modal is "1" ? "modal" : "page", false);

    [HttpGet("/duvarinda-gor/karsilastir")]
    public async Task<IActionResult> Compare(string? products, string? scene)
    {
        var slugs = (products ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Take(4).ToList();
        var details = new List<WallProductDetailDto>();
        foreach (var slug in slugs)
            if (await sender.Send(new GetWallProductDetailQuery(slug)) is { } d) details.Add(d);
        if (details.Count < 2)
            return await NotFoundPageAsync("Karşılaştırmak için en az 2 geçerli poster seçin.");

        var scenes = await sender.Send(new GetRoomScenesQuery(await WallVisitor.GetOwnerKeyAsync(HttpContext, sender)));
        if (scenes.Count == 0) await EnsureScenesAsync();
        scenes = scenes.Count > 0 ? scenes : await sender.Send(new GetRoomScenesQuery());
        var settings = await sender.Send(new GetWallCoveringSettingsQuery());
        var selected = PickScene(scenes, scene, settings);

        var materials = await sender.Send(new GetMaterialsQuery());
        var input = WallConfigurationInput.FromQuery(key => Request.Query.TryGetValue(key, out var v) ? v.ToString() : null);
        var configuration = input.Parse(materials[0].Code, settings.DefaultWidthCm, settings.DefaultHeightCm, materials.Select(m => m.Code).ToList());
        return View(new WallCompareModel(details, selected, scenes, configuration, materials));
    }

    private async Task<IActionResult> RenderAsync(string? slug, string? sceneParam, string? alignParam, string? returnUrl, string mode, bool openList)
    {
        var product = string.IsNullOrWhiteSpace(slug) ? null : await sender.Send(new GetWallProductDetailQuery(slug));
        if (product is null) return await NotFoundPageAsync(null);

        var result = await WallVisualizerModelFactory.CreateAsync(sender, HttpContext, product, sceneParam, alignParam, returnUrl, mode, openList);
        return result.Model is null ? await NotFoundPageAsync(result.Error) : View("Index", result.Model);
    }

    private static SceneDto PickScene(IReadOnlyList<SceneDto> scenes, string? sceneParam, WallCoveringSettings settings) =>
        (Guid.TryParse(sceneParam, out var id) ? scenes.FirstOrDefault(s => s.Id == id) : null)
        ?? (settings.DefaultSceneId is Guid d ? scenes.FirstOrDefault(s => s.Id == d) : null)
        ?? scenes.FirstOrDefault(s => s.IsDefault && !s.IsUserScene)
        ?? scenes[0];

    private Task EnsureScenesAsync() => sender.Send(new EnsureDefaultRoomScenesCommand());

    private async Task<IActionResult> NotFoundPageAsync(string? message)
    {
        var suggestions = await sender.Send(new GetWallCatalogQuery(PageSize: 8));
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewBag.Message = message;
        return View("NotFound", suggestions);
    }
}
