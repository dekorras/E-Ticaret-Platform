using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Application.WallCovering;

/// <param name="IsGlue">Satır, bir malzemeye tutkal ürünü olarak bağlı düz katalog ürünü mü.</param>
/// <param name="GlueDemandUnits">Satırdaki tutkal gerektiren duvar kağıdı adedi (konfigüre satırlarda).</param>
public sealed record CartTotalsLine(decimal LineTotal, decimal TaxRatePercentage, bool IsGlue, int GlueDemandUnits, decimal UnitPrice, int Quantity);

public sealed record CartTotals(
    decimal SubTotal,
    decimal GlueDiscount,
    bool GlueEligibleForFree,
    decimal? GlueFreeRemaining,
    decimal Discount,
    bool FreeShipping,
    decimal? FreeShippingRemaining,
    decimal Tax,
    decimal Total);

/// <summary>Sepet toplamı kuralları (spec 1.4 sırası: ara toplam → tutkal → kupon → kargo eşiği → KDV).
/// Checkout (PlaceOrderCommand) aynı <see cref="GlueFreeUnits"/> ve ücretsiz kargo kuralını uygular;
/// buradaki hesap ürün sayfası/sepet ÖNİZLEMESİ içindir.
/// VARSAYIM: kupon, ücretsiz tutkal düşüldükten SONRAKİ ara toplama uygulanır (ücretsiz ürüne indirim
/// uygulanmasın diye) - spec kuponu tutkaldan önce sıralıyor ama siparişte tutkal satırı zaten 0 ₺
/// olarak yazıldığı için sonuç aynı ara toplama denk gelir.
/// VARSAYIM: KDV mevcut Order.Recalculate ile aynı şekilde satır tutarları üzerinden (indirim öncesi) hesaplanır.</summary>
public static class CartTotalsCalculator
{
    /// <summary>Kaç tutkal adedinin ücretsiz olacağı. VARSAYIM: tutkal gerektiren her duvar kağıdı adedi
    /// için 1 tutkal ücretsizdir (sınırsız ücretsiz tutkal suistimalini önlemek için).</summary>
    public static int GlueFreeUnits(decimal subTotalExcludingGlue, int glueDemandUnits, int glueQuantityInCart, decimal? glueFreeThresholdTry) =>
        glueFreeThresholdTry is decimal threshold && subTotalExcludingGlue >= threshold
            ? Math.Min(glueDemandUnits, glueQuantityInCart)
            : 0;

    public static bool IsFreeShipping(decimal subTotalAfterDiscount, decimal? freeShippingThresholdTry) =>
        freeShippingThresholdTry is decimal threshold && subTotalAfterDiscount >= threshold;

    public static CartTotals Compute(IReadOnlyList<CartTotalsLine> lines, IDiscountSource? discountSource, WallCoveringSettings settings)
    {
        var subTotalExcludingGlue = lines.Where(l => !l.IsGlue).Sum(l => l.LineTotal);
        var glueDemand = lines.Sum(l => l.GlueDemandUnits);
        var glueQuantity = lines.Where(l => l.IsGlue).Sum(l => l.Quantity);
        var freeUnits = GlueFreeUnits(subTotalExcludingGlue, glueDemand, glueQuantity, settings.GlueFreeThresholdTry);

        // Ücretsiz adetler, sepetteki tutkal satırlarına sırayla dağıtılır; KDV de ücretsiz adetlerden düşer.
        var glueDiscount = 0m;
        var glueTaxReduction = 0m;
        var remainingFree = freeUnits;
        foreach (var glueLine in lines.Where(l => l.IsGlue))
        {
            var free = Math.Min(remainingFree, glueLine.Quantity);
            glueDiscount += free * glueLine.UnitPrice;
            glueTaxReduction += free * glueLine.UnitPrice * glueLine.TaxRatePercentage / 100m;
            remainingFree -= free;
        }

        var subTotal = lines.Sum(l => l.LineTotal) - glueDiscount;
        var discount = Math.Min(discountSource?.CalculateDiscount(subTotal) ?? 0m, subTotal);
        var afterDiscount = subTotal - discount;
        var tax = Math.Round(lines.Sum(l => l.LineTotal * l.TaxRatePercentage / 100m) - glueTaxReduction, 2, MidpointRounding.AwayFromZero);

        var freeShipping = IsFreeShipping(afterDiscount, settings.FreeShippingThresholdTry);

        return new CartTotals(
            SubTotal: subTotal,
            GlueDiscount: glueDiscount,
            GlueEligibleForFree: settings.GlueFreeThresholdTry is decimal gt && subTotalExcludingGlue >= gt,
            GlueFreeRemaining: settings.GlueFreeThresholdTry is decimal g ? Math.Max(0m, g - subTotalExcludingGlue) : null,
            Discount: discount,
            FreeShipping: freeShipping,
            FreeShippingRemaining: settings.FreeShippingThresholdTry is decimal s ? Math.Max(0m, s - afterDiscount) : null,
            Tax: tax,
            Total: afterDiscount + tax);
    }

    /// <summary>Tutkal olarak işaretlenmiş (herhangi bir malzemenin GlueProductId'si olan) ürünler.</summary>
    public static HashSet<Guid> GlueProductIds(IUnitOfWork unitOfWork) =>
        unitOfWork.Repository<Material>().Query()
            .Where(m => m.GlueProductId != null)
            .Select(m => m.GlueProductId!.Value)
            .ToHashSet();

    /// <summary>Konfigüre satırın malzemesi tutkal gerektiriyor mu (JSON'daki malzeme koduna göre).</summary>
    public static Func<string?, bool> RequiresGlueResolver(IUnitOfWork unitOfWork)
    {
        var glueMaterials = unitOfWork.Repository<Material>().Query().Where(m => m.RequiresGlue).Select(m => m.Code).ToHashSet();
        return json => WallConfiguration.FromJson(json) is { } config && glueMaterials.Contains(config.MaterialCode);
    }

    public static List<CartTotalsLine> LoadCartLines(IUnitOfWork unitOfWork, string? sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey)) return [];

        var cartId = unitOfWork.Repository<Cart>().Query().Where(c => c.SessionKey == sessionKey).Select(c => (Guid?)c.Id).FirstOrDefault();
        if (cartId is null) return [];

        var rows = unitOfWork.Repository<CartItem>().Query()
            .Where(i => i.CartId == cartId)
            .Join(unitOfWork.Repository<Product>().Query(), i => i.ProductId, p => p.Id,
                (i, p) => new { i.ProductId, i.Quantity, i.UnitPriceTry, i.ConfigHash, i.ConfigurationJson, p.TaxRatePercentage })
            .ToList();

        var glueIds = GlueProductIds(unitOfWork);
        var requiresGlue = RequiresGlueResolver(unitOfWork);

        return rows.Select(r => new CartTotalsLine(
            r.UnitPriceTry * r.Quantity,
            r.TaxRatePercentage,
            IsGlue: r.ConfigHash == null && glueIds.Contains(r.ProductId),
            GlueDemandUnits: r.ConfigHash != null && requiresGlue(r.ConfigurationJson) ? r.Quantity : 0,
            r.UnitPriceTry,
            r.Quantity)).ToList();
    }

    /// <summary>GetCartQuery ile aynı öncelik: geçerli kupon, yoksa koşulu sağlayan en iyi kampanya.</summary>
    public static IDiscountSource? LoadValidCoupon(IUnitOfWork unitOfWork, string? sessionKey, DateTime nowUtc)
    {
        var couponCode = string.IsNullOrEmpty(sessionKey)
            ? null
            : unitOfWork.Repository<Cart>().Query().Where(c => c.SessionKey == sessionKey).Select(c => c.CouponCode).FirstOrDefault();

        if (couponCode is not null)
        {
            var coupon = unitOfWork.Repository<Coupon>().Query().FirstOrDefault(c => c.Code == couponCode);
            return coupon is not null && coupon.IsValidNow(nowUtc) ? new CouponDiscount(coupon) : null;
        }

        var campaigns = unitOfWork.Repository<Campaign>().Query()
            .Where(c => c.IsActive && nowUtc >= c.StartsAtUtc && nowUtc <= c.EndsAtUtc)
            .ToList()
            .Where(c => c.UsageLimit is null || c.UsageCount < c.UsageLimit)
            .ToList();
        return campaigns.Count == 0 ? null : new BestCampaignDiscount(campaigns);
    }
}

public interface IDiscountSource
{
    decimal CalculateDiscount(decimal subTotal);
}

internal sealed class CouponDiscount(Coupon coupon) : IDiscountSource
{
    public decimal CalculateDiscount(decimal subTotal) => coupon.CalculateDiscount(subTotal);
}

internal sealed class BestCampaignDiscount(IReadOnlyList<Campaign> campaigns) : IDiscountSource
{
    public decimal CalculateDiscount(decimal subTotal) =>
        campaigns.Where(c => c.MeetsRules(subTotal)).Select(c => c.CalculateDiscount(subTotal)).DefaultIfEmpty(0m).Max();
}
