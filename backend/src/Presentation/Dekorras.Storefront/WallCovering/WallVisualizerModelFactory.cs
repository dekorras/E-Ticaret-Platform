using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using Dekorras.Storefront.Models;
using MediatR;

namespace Dekorras.Storefront.WallCovering;

/// <summary>Görüntüleyici modelini kurar - tam sayfa/modal (WallPreviewController) ve gömülü widget
/// (EmbedController) aynı kuralları kullanır: geçersiz parametre varsayılana düşer, varsayılan ölçü ürünün
/// en-boy oranına uyar, sahne yoksa prosedürel varsayılan sahneler üretilir.</summary>
public static class WallVisualizerModelFactory
{
    public sealed record Result(WallVisualizerModel? Model, string? Error);

    public static async Task<Result> CreateAsync(ISender sender, HttpContext http, WallProductDetailDto product, string? sceneParam, string? alignParam,
        string? returnUrl, string mode, bool openList, string? parentOrigin = null, bool external = false)
    {
        var materials = await sender.Send(new GetMaterialsQuery(external ? null : product.ProductId));
        if (materials.Count == 0) return new Result(null, "Bu ürün için şu anda satışta malzeme yok.");

        // Gömülü modda (üçüncü taraf iframe) çerezler çoğu tarayıcıda engellenir; kullanıcı sahneleri listelenmez.
        var ownerKey = mode == "embed" ? null : await WallVisitor.GetOwnerKeyAsync(http, sender);
        var scenes = await sender.Send(new GetRoomScenesQuery(ownerKey));
        if (scenes.Count == 0)
        {
            await sender.Send(new EnsureDefaultRoomScenesCommand());
            scenes = await sender.Send(new GetRoomScenesQuery(ownerKey));
        }
        if (scenes.Count == 0) return new Result(null, "Şu anda gösterilecek oda sahnesi yok.");

        var settings = await sender.Send(new GetWallCoveringSettingsQuery());
        var scene = (Guid.TryParse(sceneParam, out var id) ? scenes.FirstOrDefault(s => s.Id == id) : null)
                    ?? (settings.DefaultSceneId is Guid d ? scenes.FirstOrDefault(s => s.Id == d) : null)
                    ?? scenes.FirstOrDefault(s => s.IsDefault && !s.IsUserScene)
                    ?? scenes[0];

        var defaultWidth = settings.DefaultWidthCm;
        var defaultHeight = product.AspectRatio > 0 ? Math.Round(defaultWidth / product.AspectRatio, 1) : settings.DefaultHeightCm;
        var input = WallConfigurationInput.FromQuery(key => http.Request.Query.TryGetValue(key, out var v) ? v.ToString() : null);
        var configuration = input.Parse(materials[0].Code, defaultWidth, defaultHeight, materials.Select(m => m.Code).ToList());

        var material = materials.First(m => m.Code == configuration.MaterialCode);
        if (WallDimensions.Validate(configuration.WidthCm, configuration.HeightCm, material.MaxHeightCm).Count > 0)
        {
            configuration = new WallConfiguration(defaultWidth, Math.Clamp(defaultHeight, WallDimensions.MinSideCm, material.MaxHeightCm),
                configuration.MaterialCode, configuration.Unit, configuration.Fit, configuration.Mirror, configuration.Filter, configuration.Crop);
        }

        return new Result(new WallVisualizerModel(product, materials, scenes, scene, configuration, WallApi.ParseAlign(alignParam),
            WallPreviewUrl.SafeReturnUrl(returnUrl), mode, openList, settings, parentOrigin, external), null);
    }
}
