using System.Text.Json;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record GetCartQuery(string SessionKey, string LanguageCode) : IRequest<CartDto>;

public sealed class GetCartQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCartQuery, CartDto>
{
    public Task<CartDto> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        // Not: cart.Items gezinme özelliğine burada DOKUNULMAZ (Include olmadan boş dönerdi -
        // bkz. GetCategoryTreeQuery'de bulunan hata). Bunun yerine CartItem'lar Cart.Id (skaler,
        // navigation değil) üzerinden ayrı bir IQueryable ile, Product'la JOIN edilerek okunur.
        var cart = unitOfWork.Repository<Cart>().Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null)
            return Task.FromResult(new CartDto(Guid.Empty, [], 0m, null, 0m, 0m));

        var rows = unitOfWork.Repository<CartItem>().Query()
            .Where(i => i.CartId == cart.Id)
            .Join(unitOfWork.Repository<Product>().Query(),
                i => i.ProductId,
                p => p.Id,
                (i, p) => new
                {
                    Dto = new CartItemDto(
                        i.ProductId,
                        i.VariantId,
                        p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                        i.VariantId != null
                            ? unitOfWork.Repository<ProductVariant>().Query().Where(v => v.Id == i.VariantId).Select(v => v.OptionName).FirstOrDefault()
                            : null,
                        p.Slug,
                        i.UnitPriceTry,
                        i.Quantity,
                        i.UnitPriceTry * i.Quantity,
                        i.Id,
                        null,
                        false,
                        p.TaxRatePercentage),
                    i.ConfigurationJson,
                    i.PriceSnapshotJson,
                })
            .ToList();

        // Ölçüye özel satırlar: konfigürasyon özeti JSON'dan bellekte çözülür (SQL'e çevrilemez).
        var materialNames = unitOfWork.Repository<Material>().Query().ToDictionary(m => m.Code, m => m.Name);
        // Konfigüratörden ÖNCE ölçüsüz eklenmiş duvar kağıdı/poster satırları (eski sabit fiyat): ürün sayfasındaki
        // m² fiyatıyla çelişir, bu yüzden toplamlara katılmaz ve siparişe dönüştürülemez (PlaceOrderCommand reddeder).
        var legacyWall = LegacyWallProductIds(unitOfWork, rows.Where(r => r.ConfigurationJson is null).Select(r => r.Dto.ProductId));
        var items = rows.Select(r => r.ConfigurationJson is null
            ? (legacyWall.Contains(r.Dto.ProductId) ? r.Dto with { RequiresConfiguration = true } : r.Dto)
            : r.Dto with { Configuration = ToConfigurationDto(r.ConfigurationJson, r.PriceSnapshotJson, materialNames) }).ToList();

        var subTotal = items.Where(i => !i.RequiresConfiguration).Sum(i => i.LineTotalTry);

        // Tutkal kuralı (bkz. CartTotalsCalculator): eşik aşılınca tutkal gerektiren her duvar kağıdı
        // adedi için 1 tutkal ücretsiz. Checkout (PlaceOrderCommand) aynı kuralı bağımsız uygular.
        var settings = WallCoveringSettings.Load(unitOfWork);
        var glueProductIds = CartTotalsCalculator.GlueProductIds(unitOfWork);
        var requiresGlue = CartTotalsCalculator.RequiresGlueResolver(unitOfWork);
        var glueLines = items.Where(i => i.Configuration is null && glueProductIds.Contains(i.ProductId)).ToList();
        var glueDemand = items.Where(i => i.Configuration is not null && requiresGlue(i.Configuration.ConfigurationJson)).Sum(i => i.Quantity);
        var freeGlueUnits = CartTotalsCalculator.GlueFreeUnits(subTotal - glueLines.Sum(i => i.LineTotalTry), glueDemand, glueLines.Sum(i => i.Quantity), settings.GlueFreeThresholdTry);
        var glueDiscount = 0m;
        foreach (var glueLine in glueLines)
        {
            var free = Math.Min(freeGlueUnits, glueLine.Quantity);
            glueDiscount += free * glueLine.UnitPriceTry;
            freeGlueUnits -= free;
        }

        var glueSuggestions = BuildGlueSuggestions(items, glueProductIds, subTotal - glueLines.Sum(i => i.LineTotalTry), settings);

        // KDV: PlaceOrderCommand/Order.Recalculate ile AYNI kural - her satır kendi oranıyla, indirimden ÖNCE;
        // ücretsiz verilen tutkal adetleri siparişte 0 ₺'lik ayrı satır olduğu için KDV'siz.
        var taxTotal = items.Where(i => !i.RequiresConfiguration).Sum(i => i.LineTotalTry * i.TaxRatePercentage / 100m);
        var freeGlueLeft = CartTotalsCalculator.GlueFreeUnits(subTotal - glueLines.Sum(i => i.LineTotalTry), glueDemand, glueLines.Sum(i => i.Quantity), settings.GlueFreeThresholdTry);
        foreach (var glueLine in glueLines)
        {
            var free = Math.Min(freeGlueLeft, glueLine.Quantity);
            taxTotal -= free * glueLine.UnitPriceTry * glueLine.TaxRatePercentage / 100m;
            freeGlueLeft -= free;
        }
        taxTotal = Math.Round(taxTotal, 2, MidpointRounding.AwayFromZero);

        var discountBase = subTotal - glueDiscount;
        var discount = 0m;
        string? campaignName = null;
        if (cart.CouponCode is not null)
        {
            var coupon = unitOfWork.Repository<Coupon>().Query().FirstOrDefault(c => c.Code == cart.CouponCode);
            if (coupon is not null && coupon.IsValidNow(DateTime.UtcNow))
                discount = coupon.CalculateDiscount(discountBase);
        }
        else if (items.Count > 0)
        {
            // Yalnızca ÖNİZLEME - PlaceOrderCommand checkout anında AYNI mantığı bağımsız olarak
            // tekrar çalıştırıp asıl indirimi uygular (bkz. Campaign belgesi - kupon ile karşılıklı dışlar).
            var now = DateTime.UtcNow;
            var bestCampaign = unitOfWork.Repository<Campaign>().Query()
                .Where(c => c.IsActive && now >= c.StartsAtUtc && now <= c.EndsAtUtc)
                .ToList()
                .Where(c => (c.UsageLimit is null || c.UsageCount < c.UsageLimit) && c.MeetsRules(discountBase))
                .OrderByDescending(c => c.CalculateDiscount(discountBase))
                .FirstOrDefault();

            if (bestCampaign is not null)
            {
                discount = bestCampaign.CalculateDiscount(discountBase);
                campaignName = bestCampaign.Name;
            }
        }

        // Hediye çeki bir İNDİRİM değil bir ÖDEME yöntemidir - kupon/kampanyayla BİRLİKTE
        // uygulanabilir (bkz. Order.ApplyGiftVoucher belgesi). Bu yalnızca bir ÖNİZLEME; kargo
        // ücreti henüz bilinmediği için gerçek kısıt (kargo dahil toplamı aşmama) yalnızca
        // PlaceOrderCommand'da uygulanır.
        var giftVoucherAmountApplied = 0m;
        var totalAfterDiscount = discountBase - discount;
        var payable = totalAfterDiscount + taxTotal; // KDV dahil (kargo ödeme adımında eklenir)
        if (cart.GiftVoucherCode is not null && payable > 0)
        {
            var giftVoucher = unitOfWork.Repository<GiftVoucher>().Query().FirstOrDefault(v => v.Code == cart.GiftVoucherCode);
            if (giftVoucher is not null && giftVoucher.IsUsable())
                giftVoucherAmountApplied = Math.Min(giftVoucher.RemainingBalanceTry, payable);
        }

        return Task.FromResult(new CartDto(
            cart.Id, items, subTotal, cart.CouponCode, discount, payable - giftVoucherAmountApplied,
            campaignName, cart.GiftVoucherCode, giftVoucherAmountApplied,
            GlueDiscountTry: glueDiscount,
            FreeShippingRemainingTry: settings.FreeShippingThresholdTry is decimal threshold ? Math.Max(0m, threshold - totalAfterDiscount) : null,
            FreeShipping: CartTotalsCalculator.IsFreeShipping(totalAfterDiscount, settings.FreeShippingThresholdTry),
            GlueSuggestions: glueSuggestions,
            TaxTotalTry: taxTotal));
    }

    /// <summary>Verilen (ölçüsüz satırlardaki) ürünlerden ölçüye özel konfigüre edilebilir olanlar.</summary>
    public static HashSet<Guid> LegacyWallProductIds(IUnitOfWork unitOfWork, IEnumerable<Guid> plainLineProductIds)
    {
        var ids = plainLineProductIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return unitOfWork.Repository<WallpaperProfile>().Query()
            .Where(p => ids.Contains(p.ProductId) && p.IsEnabled)
            .Select(p => p.ProductId)
            .ToHashSet();
    }

    private static CartItemConfigurationDto? ToConfigurationDto(string configurationJson, string? priceSnapshotJson, IReadOnlyDictionary<string, string> materialNames)
    {
        var config = WallConfiguration.FromJson(configurationJson);
        if (config is null) return null;

        WallpaperLinePrice? snapshot = null;
        try { snapshot = string.IsNullOrEmpty(priceSnapshotJson) ? null : JsonSerializer.Deserialize<WallpaperLinePrice>(priceSnapshotJson); }
        catch (JsonException) { /* bozuk anlık görüntü yalnızca gösterimi etkiler */ }

        return new CartItemConfigurationDto(
            config.WidthCm, config.HeightCm, WallDimensions.UnitCode(config.Unit), config.MaterialCode,
            materialNames.TryGetValue(config.MaterialCode, out var name) ? name : config.MaterialCode,
            config.Fit.ToString(), config.Mirror, config.Filter.ToString(), config.Crop?.ToQueryValue(),
            snapshot?.BilledAreaM2 ?? 0m, snapshot?.PanelCount ?? 0, configurationJson);
    }

    /// <summary>Tutkal gerektiren malzemeyle konfigüre edilmiş satırlar için, sepette henüz o
    /// malzemenin tutkal ürünü yoksa öneri üretir (spec 1.8 - Tutkal upsell).</summary>
    private List<GlueSuggestionDto> BuildGlueSuggestions(IReadOnlyList<CartItemDto> items, HashSet<Guid> glueProductIds, decimal subTotalExcludingGlue, WallCoveringSettings settings)
    {
        var demandByMaterial = items
            .Where(i => i.Configuration is not null)
            .GroupBy(i => i.Configuration!.MaterialCode)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (demandByMaterial.Count == 0) return [];

        var codes = demandByMaterial.Keys.ToList();
        var glueByMaterial = unitOfWork.Repository<Material>().Query()
            .Where(m => codes.Contains(m.Code) && m.RequiresGlue && m.GlueProductId != null)
            .Select(m => new { m.Code, GlueProductId = m.GlueProductId!.Value })
            .ToList();

        var inCart = items.Where(i => i.Configuration is null && glueProductIds.Contains(i.ProductId)).Select(i => i.ProductId).ToHashSet();
        var willBeFree = settings.GlueFreeThresholdTry is decimal t && subTotalExcludingGlue >= t;

        return glueByMaterial
            .GroupBy(g => g.GlueProductId)
            .Where(g => !inCart.Contains(g.Key))
            .Select(g =>
            {
                var product = unitOfWork.Repository<Product>().Query()
                    .Where(p => p.Id == g.Key && p.Status == ProductStatus.Active)
                    .Select(p => new
                    {
                        p.Slug,
                        p.BasePriceTry,
                        Name = p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault() ?? p.ProductCode
                    })
                    .FirstOrDefault();
                return product is null
                    ? null
                    : new GlueSuggestionDto(g.Key, product.Name, product.Slug, product.BasePriceTry, g.Sum(x => demandByMaterial[x.Code]), willBeFree);
            })
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }
}
