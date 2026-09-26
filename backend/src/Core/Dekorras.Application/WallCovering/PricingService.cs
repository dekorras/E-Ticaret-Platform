using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Application.WallCovering;

/// <summary>Tek bir konfigüre satırın sunucu tarafı fiyat sonucu. <see cref="Errors"/> boş değilse
/// <see cref="Line"/> null'dur; hatalar alan adına göre Türkçe mesajlardır (width/height/material/product).</summary>
public sealed record WallLineQuote(
    IReadOnlyDictionary<string, string> Errors,
    WallpaperLinePrice? Line,
    Material? Material,
    decimal TaxRatePercentage,
    string ProductName)
{
    public bool IsValid => Errors.Count == 0 && Line is not null;
}

/// <summary>POST /pricing/quote yanıtı (spec 1.4 - kırılım). Tutarlar KDV HARİÇ birim fiyatlardan
/// hesaplanır, KDV ayrıca eklenir (mevcut sistemle aynı). Kargo, sağlayıcı checkout'ta seçildiği
/// için yalnızca ücretsiz kargo eşiği aşılmışsa 0, aksi halde null ("checkout'ta hesaplanır").</summary>
public sealed record WallQuoteDto(
    bool Gecerli,
    IReadOnlyDictionary<string, string> Hatalar,
    string? MalzemeKodu,
    string? MalzemeAdi,
    decimal EnCm,
    decimal BoyCm,
    decimal AlanM2,
    decimal FaturaM2,
    int PanelSayisi,
    decimal PanelGenisligi,
    decimal KesimPayiCm,
    bool KesimPayiDahil,
    decimal BirimM2Fiyati,
    decimal BirimFiyat,
    int Adet,
    decimal AraToplam,
    decimal Indirim,
    decimal? Kargo,
    decimal? Tutkal,
    bool TutkalUcretsiz,
    decimal Kdv,
    decimal Toplam,
    decimal? UcretsizKargoIcinKalan,
    decimal? UcretsizTutkalIcinKalan,
    bool TutkalGerekir,
    DateOnly? TahminiTeslimEnErken,
    DateOnly? TahminiTeslimEnGec);

public interface IPricingService
{
    /// <summary>Ürünün ölçüye özel konfigüre edilip edilemeyeceği (WallpaperProfile var ve etkin mi).</summary>
    bool IsConfigurable(Guid productId);

    WallLineQuote QuoteLine(Guid productId, WallConfiguration configuration, int quantity);

    /// <summary>Satır + (varsa) sepetin geri kalanıyla birlikte eşik/kupon/KDV kırılımı.</summary>
    WallQuoteDto Quote(Guid productId, WallConfiguration configuration, int quantity, string? sessionKey, DateTime nowUtc);
}

public sealed class PricingService(IUnitOfWork unitOfWork) : IPricingService
{
    public bool IsConfigurable(Guid productId) =>
        unitOfWork.Repository<WallpaperProfile>().Query().Any(p => p.ProductId == productId && p.IsEnabled);

    public WallLineQuote QuoteLine(Guid productId, WallConfiguration configuration, int quantity)
    {
        var errors = new Dictionary<string, string>();

        var product = unitOfWork.Repository<Product>().Query()
            .Where(p => p.Id == productId)
            .Select(p => new
            {
                p.Status,
                p.TaxRatePercentage,
                p.ProductCode,
                Name = p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault()
            })
            .FirstOrDefault();

        if (product is null || product.Status != ProductStatus.Active)
            return Invalid("product", "Ürün bulunamadı veya satışa kapalı.");
        if (!IsConfigurable(productId))
            return Invalid("product", "Bu ürün ölçüye özel sipariş edilemez.");

        var material = unitOfWork.Repository<Material>().Query().FirstOrDefault(m => m.Code == configuration.MaterialCode && m.IsActive);
        if (material is null)
            return Invalid("material", "Seçilen malzeme bulunamadı veya satışta değil.");

        var materialOverride = unitOfWork.Repository<ProductMaterialOverride>().Query()
            .FirstOrDefault(o => o.ProductId == productId && o.MaterialId == material.Id);
        if (materialOverride is { IsAllowed: false })
            return Invalid("material", "Seçilen malzeme bu ürün için kullanılamaz.");

        foreach (var (key, message) in WallDimensions.Validate(configuration.WidthCm, configuration.HeightCm, material.MaxHeightCm))
            errors[key] = message;
        if (quantity < 1 || quantity > 99)
            errors["quantity"] = "Adet 1 ile 99 arasında olmalıdır.";
        if (errors.Count > 0)
            return new WallLineQuote(errors, null, material, product.TaxRatePercentage, product.Name ?? product.ProductCode);

        var settings = WallCoveringSettings.Load(unitOfWork);
        var line = WallpaperPriceCalculator.Calculate(
            configuration.WidthCm, configuration.HeightCm, quantity, MaterialPricing.From(material, materialOverride?.PricePerM2), settings.ChargeBleed);

        return new WallLineQuote(errors, line, material, product.TaxRatePercentage, product.Name ?? product.ProductCode);

        WallLineQuote Invalid(string key, string message) =>
            new(new Dictionary<string, string> { [key] = message }, null, null, 0m, product?.Name ?? "");
    }

    public WallQuoteDto Quote(Guid productId, WallConfiguration configuration, int quantity, string? sessionKey, DateTime nowUtc)
    {
        var lineQuote = QuoteLine(productId, configuration, quantity);
        var settings = WallCoveringSettings.Load(unitOfWork);
        var delivery = DeliveryEstimator.Estimate(nowUtc, extraHolidays: settings.ExtraHolidays);

        if (!lineQuote.IsValid)
        {
            return new WallQuoteDto(false, lineQuote.Errors, lineQuote.Material?.Code, lineQuote.Material?.Name, configuration.WidthCm, configuration.HeightCm,
                0, 0, 0, lineQuote.Material?.PanelWidthCm ?? 0, lineQuote.Material?.BleedCm ?? 0, settings.ChargeBleed, lineQuote.Material?.PricePerM2 ?? 0,
                0, quantity, 0, 0, null, null, false, 0, 0, null, null, lineQuote.Material?.RequiresGlue ?? false, delivery.EarliestDelivery, delivery.LatestDelivery);
        }

        var line = lineQuote.Line!;
        var material = lineQuote.Material!;

        // Sepetin geri kalanı: eşikler (ücretsiz kargo/tutkal) ve kupon sepet TOPLAMINA göre değerlendirilir.
        var cartLines = CartTotalsCalculator.LoadCartLines(unitOfWork, sessionKey);
        cartLines.Add(new CartTotalsLine(line.LineTotal, lineQuote.TaxRatePercentage, IsGlue: false, GlueDemandUnits: material.RequiresGlue ? quantity : 0, UnitPrice: line.UnitPrice, Quantity: quantity));

        var coupon = CartTotalsCalculator.LoadValidCoupon(unitOfWork, sessionKey, nowUtc);
        var totals = CartTotalsCalculator.Compute(cartLines, coupon, settings);

        // Tutkal önerisi: malzeme tutkal gerektiriyorsa, sepette tutkal yoksa bile önerilecek ürünün fiyatı gösterilir.
        decimal? glueUnitPrice = null;
        if (material.RequiresGlue && material.GlueProductId is Guid glueProductId)
            glueUnitPrice = unitOfWork.Repository<Product>().Query().Where(p => p.Id == glueProductId && p.Status == ProductStatus.Active)
                .Select(p => (decimal?)p.BasePriceTry).FirstOrDefault();

        var glueFree = material.RequiresGlue && totals.GlueEligibleForFree;

        return new WallQuoteDto(
            Gecerli: true,
            Hatalar: lineQuote.Errors,
            MalzemeKodu: material.Code,
            MalzemeAdi: material.Name,
            EnCm: line.WidthCm,
            BoyCm: line.HeightCm,
            AlanM2: line.AreaM2,
            FaturaM2: line.BilledAreaM2,
            PanelSayisi: line.PanelCount,
            PanelGenisligi: line.PanelWidthCm,
            KesimPayiCm: line.BleedCm,
            KesimPayiDahil: line.BleedCharged,
            BirimM2Fiyati: line.UnitPricePerM2,
            BirimFiyat: line.UnitPrice,
            Adet: quantity,
            AraToplam: line.LineTotal,
            Indirim: totals.Discount,
            Kargo: totals.FreeShipping ? 0m : null,
            Tutkal: glueUnitPrice is null ? null : glueFree ? 0m : glueUnitPrice,
            TutkalUcretsiz: glueFree,
            Kdv: totals.Tax,
            Toplam: totals.Total,
            UcretsizKargoIcinKalan: totals.FreeShippingRemaining,
            UcretsizTutkalIcinKalan: material.RequiresGlue ? totals.GlueFreeRemaining : null,
            TutkalGerekir: material.RequiresGlue,
            TahminiTeslimEnErken: delivery.EarliestDelivery,
            TahminiTeslimEnGec: delivery.LatestDelivery);
    }
}
