using System.Text.Json;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Storefront.Models;

/// <summary>Ürün sayfasındaki ölçüye özel konfigüratör (spec 1.5). <see cref="Initial"/> URL
/// parametrelerinden sunucuda doğrulanarak üretilir (geçersiz değerler varsayılana düşer) ve JS'e
/// <see cref="ToClientJson"/> ile tek bir JSON bloğu olarak verilir.</summary>
public sealed record WallConfiguratorModel(
    WallProductDetailDto Product,
    IReadOnlyList<MaterialDto> Materials,
    WallConfiguration Initial,
    int InitialQuantity,
    WallQuoteDto InitialQuote,
    WallCoveringSettings Settings,
    DeliveryEstimateDto Delivery,
    string CanonicalUrl)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MaterialDto? SelectedMaterial => Materials.FirstOrDefault(m => m.Code == Initial.MaterialCode);

    public string ToClientJson() => JsonSerializer.Serialize(new
    {
        product = new
        {
            Product.ProductId,
            Product.Slug,
            Product.Title,
            Product.PreviewUrl,
            Product.ImageWidthPx,
            Product.ImageHeightPx,
            Product.AspectRatio,
            Product.ProductType,
            Product.RepeatWidthCm,
            Product.RepeatHeightCm,
            Product.RepeatType,
            Product.TaxRatePercentage
        },
        materials = Materials,
        initial = new
        {
            material = Initial.MaterialCode,
            unit = WallDimensions.UnitCode(Initial.Unit),
            wCm = Initial.WidthCm,
            hCm = Initial.HeightCm,
            fit = Initial.Fit == FitMode.Stretch ? "stretch" : "crop",
            mirror = Initial.Mirror,
            filter = Initial.Filter.ToString().ToLowerInvariant(),
            crop = Initial.Crop is null ? null : new[] { Initial.Crop.X, Initial.Crop.Y, Initial.Crop.W, Initial.Crop.H },
            quantity = InitialQuantity
        },
        rules = new
        {
            minSideCm = WallDimensions.MinSideCm,
            maxWidthCm = WallDimensions.MaxWidthCm,
            chargeBleed = Settings.ChargeBleed,
            minPrintDpi = Settings.MinPrintDpi,
            freeShippingThreshold = Settings.FreeShippingThresholdTry
        },
        quote = InitialQuote
    }, JsonOptions);
}
