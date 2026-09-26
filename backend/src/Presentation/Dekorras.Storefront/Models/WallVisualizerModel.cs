using System.Text.Json;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using Dekorras.Storefront.WallCovering;

namespace Dekorras.Storefront.Models;

/// <summary>Duvar görüntüleyici sayfası (spec 1.6.3). Hem tam sayfa hem modal (iframe, ?modal=1)
/// hem de gömülü (embed) modda aynı model ve aynı JS modülü (wall-visualizer.js) kullanılır.</summary>
public sealed record WallVisualizerModel(
    WallProductDetailDto Product,
    IReadOnlyList<MaterialDto> Materials,
    IReadOnlyList<SceneDto> Scenes,
    SceneDto Scene,
    WallConfiguration Configuration,
    WallAlign Align,
    string? ReturnUrl,
    string Mode,
    bool OpenListTab,
    WallCoveringSettings Settings,
    string? EmbedParentOrigin = null,
    bool External = false)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsModal => Mode == "modal";
    public bool IsEmbed => Mode == "embed";

    public string AlignCode => Align switch { WallAlign.Left => "left", WallAlign.Right => "right", _ => "center" };

    /// <summary>JS'siz yedek: aynı durumun sunucu render'ı.</summary>
    public string ServerRenderUrl(int size = 1200) =>
        $"/api/v1/scenes/{Scene.Id}/render?product={Uri.EscapeDataString(Product.Slug)}&align={AlignCode}&size={size}&" +
        string.Join("&", WallConfigurationInput.ToQuery(Configuration).Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

    public string ShareUrl(HttpRequest request) =>
        WallPreviewUrl.Build(Product.Slug, Configuration, Scene.Id, AlignCode, null, null, $"{request.Scheme}://{request.Host}");

    public string ToClientJson() => JsonSerializer.Serialize(new
    {
        product = Product,
        materials = Materials,
        scenes = Scenes,
        sceneId = Scene.Id,
        initial = new
        {
            material = Configuration.MaterialCode,
            unit = WallDimensions.UnitCode(Configuration.Unit),
            wCm = Configuration.WidthCm,
            hCm = Configuration.HeightCm,
            fit = Configuration.Fit == FitMode.Stretch ? "stretch" : "crop",
            mirror = Configuration.Mirror,
            filter = Configuration.Filter.ToString().ToLowerInvariant(),
            crop = Configuration.Crop is null ? null : new[] { Configuration.Crop.X, Configuration.Crop.Y, Configuration.Crop.W, Configuration.Crop.H },
            align = AlignCode
        },
        rules = new { minSideCm = WallDimensions.MinSideCm, maxWidthCm = WallDimensions.MaxWidthCm, chargeBleed = Settings.ChargeBleed, minPrintDpi = Settings.MinPrintDpi },
        defaults = new { wCm = Settings.DefaultWidthCm, hCm = Settings.DefaultHeightCm, material = Materials.FirstOrDefault()?.Code },
        returnUrl = ReturnUrl,
        mode = Mode,
        openListTab = OpenListTab,
        parentOrigin = EmbedParentOrigin,
        external = External
    }, JsonOptions);
}

public sealed record WallCompareModel(IReadOnlyList<WallProductDetailDto> Products, SceneDto Scene, IReadOnlyList<SceneDto> Scenes, WallConfiguration Configuration, IReadOnlyList<MaterialDto> Materials)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToClientJson() => JsonSerializer.Serialize(new
    {
        products = Products,
        scene = Scene,
        scenes = Scenes,
        materials = Materials,
        initial = new { material = Configuration.MaterialCode, wCm = Configuration.WidthCm, hCm = Configuration.HeightCm }
    }, JsonOptions);
}
