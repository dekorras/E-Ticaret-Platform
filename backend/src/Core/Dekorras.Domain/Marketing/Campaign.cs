using Dekorras.Domain.Common;
using Dekorras.Domain.Ordering;

namespace Dekorras.Domain.Marketing;

/// <summary>
/// Kupondan farkı: müşteri bir KOD girmez, koşulları (bkz. Rules - şu an yalnızca "MinCartTotal"
/// değerlendirilir) sağlayan sepete OTOMATİK olarak uygulanır (bkz. PlaceOrderCommand). Aynı
/// checkout'ta bir kupon ZATEN uygulanmışsa kampanya bilinçli olarak atlanır (ikisinin birlikte
/// nasıl birleşeceği ayrı bir tasarım kararı gerektirir - bkz. backend/README.md).
/// </summary>
public class Campaign : AuditableEntity
{
    public string Name { get; private set; } = default!;
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public DiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public bool AllowGuestUsage { get; private set; } = true;
    public int? UsageLimit { get; private set; }
    public int UsageCount { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<PromotionRule> _rules = [];
    public IReadOnlyCollection<PromotionRule> Rules => _rules.AsReadOnly();

    private Campaign() { }

    public Campaign(string name, DateTime startsAtUtc, DateTime endsAtUtc, DiscountType discountType, decimal discountValue, int? usageLimit = null)
    {
        Name = name;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        DiscountType = discountType;
        DiscountValue = discountValue;
        UsageLimit = usageLimit;
    }

    public void AddRule(string ruleType, string ruleValueJson) =>
        _rules.Add(new PromotionRule(Id, ruleType, ruleValueJson));

    public bool IsValidNow(DateTime nowUtc) =>
        IsActive && nowUtc >= StartsAtUtc && nowUtc <= EndsAtUtc && (UsageLimit is null || UsageCount < UsageLimit);

    /// <summary>Yalnızca "MinCartTotal" kural tipi değerlendirilir (plan §5'te bahsedilen
    /// "CategoryId"/"CustomerGroup" kuralları için altyapı - Rules koleksiyonu - hazır ama
    /// değerlendirme mantığı henüz yazılmadı, bkz. backend/README.md).</summary>
    public bool MeetsRules(decimal cartSubTotalTry)
    {
        var minCartTotalRule = _rules.FirstOrDefault(r => r.RuleType == "MinCartTotal");
        if (minCartTotalRule is null) return true;

        return decimal.TryParse(minCartTotalRule.RuleValueJson, System.Globalization.CultureInfo.InvariantCulture, out var minTotal)
            && cartSubTotalTry >= minTotal;
    }

    public decimal CalculateDiscount(decimal orderSubTotalTry) =>
        DiscountType == DiscountType.Percentage ? orderSubTotalTry * DiscountValue / 100m : DiscountValue;

    public void RegisterUsage() => UsageCount++;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

public class PromotionRule : BaseEntity
{
    public Guid CampaignId { get; private set; }
    public string RuleType { get; private set; } = default!; // ör. "MinCartTotal", "CategoryId", "CustomerGroup"
    public string RuleValueJson { get; private set; } = default!;

    private PromotionRule() { }

    public PromotionRule(Guid campaignId, string ruleType, string ruleValueJson)
    {
        CampaignId = campaignId;
        RuleType = ruleType;
        RuleValueJson = ruleValueJson;
    }
}

public class NewsletterSubscriber : AuditableEntity
{
    public string Email { get; private set; } = default!;
    public bool IsConfirmed { get; private set; }

    private NewsletterSubscriber() { }

    public NewsletterSubscriber(string email) => Email = email;

    public void Confirm() => IsConfirmed = true;
}
