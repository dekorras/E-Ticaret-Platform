using Dekorras.Domain.Common;

namespace Dekorras.Domain.Marketplace;

public class MarketplaceAccount : AuditableEntity
{
    public Guid IntegrationProviderId { get; private set; } // Trendyol/Hepsiburada/N11/İdefix/Amazon - IntegrationProvider
    public string SellerAccountLabel { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private MarketplaceAccount() { }

    public MarketplaceAccount(Guid integrationProviderId, string sellerAccountLabel)
    {
        IntegrationProviderId = integrationProviderId;
        SellerAccountLabel = sellerAccountLabel;
    }
}

public class MarketplaceCategoryMapping : BaseEntity
{
    public Guid MarketplaceAccountId { get; private set; }
    public Guid LocalCategoryId { get; private set; }
    public string MarketplaceCategoryId { get; private set; } = default!;
    public string MarketplaceCategoryName { get; private set; } = default!;

    private MarketplaceCategoryMapping() { }

    public MarketplaceCategoryMapping(Guid marketplaceAccountId, Guid localCategoryId, string marketplaceCategoryId, string marketplaceCategoryName)
    {
        MarketplaceAccountId = marketplaceAccountId;
        LocalCategoryId = localCategoryId;
        MarketplaceCategoryId = marketplaceCategoryId;
        MarketplaceCategoryName = marketplaceCategoryName;
    }
}

public enum MarketplaceListingStatus { NotListed, Pending, Listed, Rejected, Removed }

public class MarketplaceListing : AuditableEntity
{
    public Guid MarketplaceAccountId { get; private set; }
    public Guid ProductId { get; private set; }
    public string MarketplaceProductId { get; private set; } = default!;
    public decimal ListedPriceTry { get; private set; }
    public MarketplaceListingStatus Status { get; private set; } = MarketplaceListingStatus.NotListed;
    public DateTime? LastSyncedAtUtc { get; private set; }

    private MarketplaceListing() { }

    public MarketplaceListing(Guid marketplaceAccountId, Guid productId, string marketplaceProductId, decimal listedPriceTry)
    {
        MarketplaceAccountId = marketplaceAccountId;
        ProductId = productId;
        MarketplaceProductId = marketplaceProductId;
        ListedPriceTry = listedPriceTry;
    }

    public void MarkSynced(MarketplaceListingStatus status)
    {
        Status = status;
        LastSyncedAtUtc = DateTime.UtcNow;
    }

    public void UpdatePrice(decimal priceTry) => ListedPriceTry = priceTry;
}

public class MarketplaceOrder : BaseEntity
{
    public Guid OrderId { get; private set; }
    public Guid MarketplaceAccountId { get; private set; }
    public string MarketplaceOrderId { get; private set; } = default!;

    private MarketplaceOrder() { }

    public MarketplaceOrder(Guid orderId, Guid marketplaceAccountId, string marketplaceOrderId)
    {
        OrderId = orderId;
        MarketplaceAccountId = marketplaceAccountId;
        MarketplaceOrderId = marketplaceOrderId;
    }
}

public class MarketplaceSyncLog : BaseEntity
{
    public Guid MarketplaceAccountId { get; private set; }
    public string Operation { get; private set; } = default!; // ör. "StockPush", "OrderPull", "PriceUpdate"
    public bool Success { get; private set; }
    public string? Message { get; private set; }
    public DateTime OccurredAtUtc { get; private set; } = DateTime.UtcNow;

    private MarketplaceSyncLog() { }

    public MarketplaceSyncLog(Guid marketplaceAccountId, string operation, bool success, string? message)
    {
        MarketplaceAccountId = marketplaceAccountId;
        Operation = operation;
        Success = success;
        Message = message;
    }
}
