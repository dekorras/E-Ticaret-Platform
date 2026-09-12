using Dekorras.Domain.Common;

namespace Dekorras.Domain.Ordering;

public class Order : AuditableEntity
{
    public string OrderNumber { get; private set; } = default!;
    public Guid CustomerId { get; private set; }
    public OrderSource Source { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.PendingApproval;

    public string CurrencyCode { get; private set; } = "TRY";
    public decimal ExchangeRateToTry { get; private set; } = 1m;
    public decimal SubTotalTry { get; private set; }
    public decimal TaxTotalTry { get; private set; }
    public decimal ShippingTotalTry { get; private set; }
    public decimal DiscountTotalTry { get; private set; }
    public decimal GrandTotalTry { get; private set; }

    public Guid ShippingAddressId { get; private set; }
    public Guid BillingAddressId { get; private set; }
    public string? CouponCode { get; private set; }
    public Guid? CampaignId { get; private set; } // otomatik uygulanan kampanya (kupon KODU girilmez) - CouponCode ile karşılıklı dışlar
    public string? GiftVoucherCode { get; private set; }
    public decimal GiftVoucherAmountAppliedTry { get; private set; } // Coupon/Campaign'in aksine bir İNDİRİM değil, ödemenin bir kısmını karşılayan bakiye düşümüdür - ikisiyle birlikte kullanılabilir
    public string? MarketplaceOrderReference { get; private set; } // pazaryerinden gelen sipariş numarası
    public string? ShipmentTrackingNumber { get; private set; }
    public Guid? ShippingProviderId { get; private set; } // seçilen kargo sağlayıcısı - Integrations.IntegrationProvider

    private readonly List<OrderItem> _items = [];
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private readonly List<OrderStatusHistory> _statusHistory = [];
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    private Order() { }

    public Order(string orderNumber, Guid customerId, OrderSource source, Guid shippingAddressId, Guid billingAddressId, string currencyCode = "TRY", decimal exchangeRateToTry = 1m)
    {
        OrderNumber = orderNumber;
        CustomerId = customerId;
        Source = source;
        ShippingAddressId = shippingAddressId;
        BillingAddressId = billingAddressId;
        CurrencyCode = currencyCode;
        ExchangeRateToTry = exchangeRateToTry;
        _statusHistory.Add(new OrderStatusHistory(Id, OrderStatus.PendingApproval, null));
    }

    public void AddItem(Guid productId, string productName, decimal unitPriceTry, int quantity, decimal taxRatePercentage, Guid? variantId = null)
    {
        _items.Add(new OrderItem(Id, productId, variantId, productName, unitPriceTry, quantity, taxRatePercentage));
        Recalculate();
    }

    public void ApplyCoupon(string couponCode, decimal discountAmountTry)
    {
        CouponCode = couponCode;
        DiscountTotalTry = discountAmountTry;
        Recalculate();
    }

    /// <summary>Bir kupon KODU girilmeden, koşulları sağlayan sepete otomatik uygulanan kampanya
    /// indirimi - PlaceOrderCommand yalnızca kupon uygulanmamışsa bunu dener (bkz. Campaign
    /// belgesi).</summary>
    public void ApplyCampaignDiscount(Guid campaignId, decimal discountAmountTry)
    {
        CampaignId = campaignId;
        DiscountTotalTry = discountAmountTry;
        Recalculate();
    }

    /// <summary>Hediye çeki bakiyesinden düşülen tutarı (checkout anında hesaplanmış, bakiyeyi
    /// AŞMAYAN) sipariş üzerinde işaretler - Coupon/Campaign indirimlerinin AKSİNE bunlar bir
    /// "listeden indirim" değil "önceden ödenmiş bir bakiyeyle kısmi ödeme" olduğu için ayrı bir
    /// alanda tutulur ve kupon/kampanyayla BİRLİKTE kullanılabilir.</summary>
    public void ApplyGiftVoucher(string giftVoucherCode, decimal amountAppliedTry)
    {
        GiftVoucherCode = giftVoucherCode;
        GiftVoucherAmountAppliedTry = amountAppliedTry;
        Recalculate();
    }

    public void SetShippingCost(Guid shippingProviderId, decimal shippingTotalTry)
    {
        ShippingProviderId = shippingProviderId;
        ShippingTotalTry = shippingTotalTry;
        Recalculate();
    }

    public void SetMarketplaceReference(string marketplaceOrderReference) =>
        MarketplaceOrderReference = marketplaceOrderReference;

    public void AttachTrackingNumber(string trackingNumber)
    {
        ShipmentTrackingNumber = trackingNumber;
        TransitionTo(OrderStatus.Shipped);
    }

    private void Recalculate()
    {
        SubTotalTry = _items.Sum(i => i.LineTotalTry);
        TaxTotalTry = _items.Sum(i => i.LineTaxTry);
        GrandTotalTry = SubTotalTry + TaxTotalTry + ShippingTotalTry - DiscountTotalTry - GiftVoucherAmountAppliedTry;
    }

    public IReadOnlyCollection<OrderStatus> GetValidNextStatuses() => OrderStatusTransitionRules.GetValidNextStates(Status);

    public void TransitionTo(OrderStatus newStatus, string? note = null)
    {
        if (!OrderStatusTransitionRules.CanTransition(Status, newStatus))
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), newStatus.ToString());

        Status = newStatus;
        _statusHistory.Add(new OrderStatusHistory(Id, newStatus, note));

        if (newStatus == OrderStatus.Completed)
            AddDomainEvent(new OrderCompletedEvent(Id, CustomerId, GrandTotalTry));
    }
}

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public string ProductName { get; private set; } = default!;
    public decimal UnitPriceTry { get; private set; }
    public int Quantity { get; private set; }
    public decimal TaxRatePercentage { get; private set; }

    public decimal LineTotalTry => UnitPriceTry * Quantity;
    public decimal LineTaxTry => LineTotalTry * TaxRatePercentage / 100m;

    private OrderItem() { }

    public OrderItem(Guid orderId, Guid productId, Guid? variantId, string productName, decimal unitPriceTry, int quantity, decimal taxRatePercentage)
    {
        OrderId = orderId;
        ProductId = productId;
        VariantId = variantId;
        ProductName = productName;
        UnitPriceTry = unitPriceTry;
        Quantity = quantity;
        TaxRatePercentage = taxRatePercentage;
    }
}

public class OrderStatusHistory : BaseEntity
{
    public Guid OrderId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Note { get; private set; }
    public DateTime ChangedAtUtc { get; private set; } = DateTime.UtcNow;

    private OrderStatusHistory() { }

    public OrderStatusHistory(Guid orderId, OrderStatus status, string? note)
    {
        OrderId = orderId;
        Status = status;
        Note = note;
    }
}
