using Dekorras.Domain.Common;

namespace Dekorras.Domain.Ordering;

public enum DiscountType { Percentage, FixedAmount }

public class Coupon : AuditableEntity
{
    public string Code { get; private set; } = default!;
    public DiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public decimal? MinimumOrderAmountTry { get; private set; }
    public DateTime ValidFromUtc { get; private set; }
    public DateTime ValidToUtc { get; private set; }
    public int? UsageLimit { get; private set; }
    public int UsageCount { get; private set; }
    public bool AllowGuestUsage { get; private set; } = true;
    public bool IsActive { get; private set; } = true;

    private Coupon() { }

    public Coupon(string code, DiscountType discountType, decimal discountValue, DateTime validFromUtc, DateTime validToUtc, int? usageLimit = null)
    {
        Code = code;
        DiscountType = discountType;
        DiscountValue = discountValue;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        UsageLimit = usageLimit;
    }

    public bool IsValidNow(DateTime nowUtc) =>
        IsActive && nowUtc >= ValidFromUtc && nowUtc <= ValidToUtc && (UsageLimit is null || UsageCount < UsageLimit);

    public decimal CalculateDiscount(decimal orderSubTotalTry) =>
        DiscountType == DiscountType.Percentage ? orderSubTotalTry * DiscountValue / 100m : DiscountValue;

    public void RegisterUsage() => UsageCount++;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

public class GiftVoucher : AuditableEntity
{
    public string Code { get; private set; } = default!;
    public decimal AmountTry { get; private set; }
    public decimal RemainingBalanceTry { get; private set; }
    public bool IsActive { get; private set; } = true;

    private GiftVoucher() { }

    public GiftVoucher(string code, decimal amountTry)
    {
        Code = code;
        AmountTry = amountTry;
        RemainingBalanceTry = amountTry;
    }

    public void Redeem(decimal amountTry)
    {
        if (amountTry > RemainingBalanceTry)
            throw new DomainException("Hediye çeki bakiyesi yetersiz.");
        RemainingBalanceTry -= amountTry;
    }

    public bool IsUsable() => IsActive && RemainingBalanceTry > 0;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
