using Dekorras.Application.Common.Interfaces;
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

        var items = unitOfWork.Repository<CartItem>().Query()
            .Where(i => i.CartId == cart.Id)
            .Join(unitOfWork.Repository<Product>().Query(),
                i => i.ProductId,
                p => p.Id,
                (i, p) => new CartItemDto(
                    i.ProductId,
                    i.VariantId,
                    p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                    i.VariantId != null
                        ? unitOfWork.Repository<ProductVariant>().Query().Where(v => v.Id == i.VariantId).Select(v => v.OptionName).FirstOrDefault()
                        : null,
                    p.Slug,
                    i.UnitPriceTry,
                    i.Quantity,
                    i.UnitPriceTry * i.Quantity))
            .ToList();

        var subTotal = items.Sum(i => i.LineTotalTry);

        var discount = 0m;
        string? campaignName = null;
        if (cart.CouponCode is not null)
        {
            var coupon = unitOfWork.Repository<Coupon>().Query().FirstOrDefault(c => c.Code == cart.CouponCode);
            if (coupon is not null && coupon.IsValidNow(DateTime.UtcNow))
                discount = coupon.CalculateDiscount(subTotal);
        }
        else if (items.Count > 0)
        {
            // Yalnızca ÖNİZLEME - PlaceOrderCommand checkout anında AYNI mantığı bağımsız olarak
            // tekrar çalıştırıp asıl indirimi uygular (bkz. Campaign belgesi - kupon ile karşılıklı dışlar).
            var now = DateTime.UtcNow;
            var bestCampaign = unitOfWork.Repository<Campaign>().Query()
                .Where(c => c.IsActive && now >= c.StartsAtUtc && now <= c.EndsAtUtc)
                .ToList()
                .Where(c => (c.UsageLimit is null || c.UsageCount < c.UsageLimit) && c.MeetsRules(subTotal))
                .OrderByDescending(c => c.CalculateDiscount(subTotal))
                .FirstOrDefault();

            if (bestCampaign is not null)
            {
                discount = bestCampaign.CalculateDiscount(subTotal);
                campaignName = bestCampaign.Name;
            }
        }

        // Hediye çeki bir İNDİRİM değil bir ÖDEME yöntemidir - kupon/kampanyayla BİRLİKTE
        // uygulanabilir (bkz. Order.ApplyGiftVoucher belgesi). Bu yalnızca bir ÖNİZLEME; kargo
        // ücreti henüz bilinmediği için gerçek kısıt (kargo dahil toplamı aşmama) yalnızca
        // PlaceOrderCommand'da uygulanır.
        var giftVoucherAmountApplied = 0m;
        var totalAfterDiscount = subTotal - discount;
        if (cart.GiftVoucherCode is not null && totalAfterDiscount > 0)
        {
            var giftVoucher = unitOfWork.Repository<GiftVoucher>().Query().FirstOrDefault(v => v.Code == cart.GiftVoucherCode);
            if (giftVoucher is not null && giftVoucher.IsUsable())
                giftVoucherAmountApplied = Math.Min(giftVoucher.RemainingBalanceTry, totalAfterDiscount);
        }

        return Task.FromResult(new CartDto(
            cart.Id, items, subTotal, cart.CouponCode, discount, totalAfterDiscount - giftVoucherAmountApplied,
            campaignName, cart.GiftVoucherCode, giftVoucherAmountApplied));
    }
}
