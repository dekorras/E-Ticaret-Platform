namespace Dekorras.Application.Ordering.Storefront;

public sealed record CartItemDto(Guid ProductId, Guid? VariantId, string Name, string? VariantOptionName, string Slug, decimal UnitPriceTry, int Quantity, decimal LineTotalTry);

public sealed record CartDto(
    Guid CartId,
    IReadOnlyCollection<CartItemDto> Items,
    decimal SubTotalTry,
    string? CouponCode,
    decimal DiscountTry,
    decimal GrandTotalTry,
    string? CampaignName = null,
    string? GiftVoucherCode = null,
    decimal GiftVoucherAmountAppliedTry = 0m);
