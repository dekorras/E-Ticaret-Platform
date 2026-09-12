using Dekorras.Application.Common.Interfaces;
using Dekorras.Infrastructure.Common;

namespace Dekorras.Infrastructure.MarketplaceConnectors;

file static class SharedFields
{
    public static readonly ConfigFieldDefinition[] Standard =
    [
        new ConfigFieldDefinition("SupplierId", "Satıcı/Tedarikçi No", ConfigFieldType.Text, true),
        new ConfigFieldDefinition("ApiKey", "API Anahtarı", ConfigFieldType.Password, true),
        new ConfigFieldDefinition("ApiSecret", "API Gizli Anahtarı", ConfigFieldType.Password, true)
    ];
}

public sealed class TrendyolConnector() : ConnectorBase("trendyol", "Trendyol", SharedFields.Standard), IMarketplaceConnector
{
    public Task<MarketplaceSyncResult> PushListingAsync(IReadOnlyDictionary<string, string> config, Guid productId, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushStockAsync(IReadOnlyDictionary<string, string> config, Guid productId, int quantity, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushPriceAsync(IReadOnlyDictionary<string, string> config, Guid productId, decimal priceTry, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<IReadOnlyCollection<MarketplaceOrderDto>> PullOrdersAsync(IReadOnlyDictionary<string, string> config, DateTime sinceUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<MarketplaceOrderDto>>([]);
    public Task<MarketplaceSyncResult> ReportShipmentAsync(IReadOnlyDictionary<string, string> config, string marketplaceOrderId, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
}

public sealed class HepsiburadaConnector() : ConnectorBase("hepsiburada", "Hepsiburada", SharedFields.Standard), IMarketplaceConnector
{
    public Task<MarketplaceSyncResult> PushListingAsync(IReadOnlyDictionary<string, string> config, Guid productId, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushStockAsync(IReadOnlyDictionary<string, string> config, Guid productId, int quantity, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushPriceAsync(IReadOnlyDictionary<string, string> config, Guid productId, decimal priceTry, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<IReadOnlyCollection<MarketplaceOrderDto>> PullOrdersAsync(IReadOnlyDictionary<string, string> config, DateTime sinceUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<MarketplaceOrderDto>>([]);
    public Task<MarketplaceSyncResult> ReportShipmentAsync(IReadOnlyDictionary<string, string> config, string marketplaceOrderId, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
}

public sealed class N11Connector() : ConnectorBase("n11", "N11", SharedFields.Standard), IMarketplaceConnector
{
    public Task<MarketplaceSyncResult> PushListingAsync(IReadOnlyDictionary<string, string> config, Guid productId, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushStockAsync(IReadOnlyDictionary<string, string> config, Guid productId, int quantity, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushPriceAsync(IReadOnlyDictionary<string, string> config, Guid productId, decimal priceTry, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<IReadOnlyCollection<MarketplaceOrderDto>> PullOrdersAsync(IReadOnlyDictionary<string, string> config, DateTime sinceUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<MarketplaceOrderDto>>([]);
    public Task<MarketplaceSyncResult> ReportShipmentAsync(IReadOnlyDictionary<string, string> config, string marketplaceOrderId, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
}

public sealed class IdefixConnector() : ConnectorBase("idefix", "İdefix", SharedFields.Standard), IMarketplaceConnector
{
    public Task<MarketplaceSyncResult> PushListingAsync(IReadOnlyDictionary<string, string> config, Guid productId, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushStockAsync(IReadOnlyDictionary<string, string> config, Guid productId, int quantity, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushPriceAsync(IReadOnlyDictionary<string, string> config, Guid productId, decimal priceTry, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<IReadOnlyCollection<MarketplaceOrderDto>> PullOrdersAsync(IReadOnlyDictionary<string, string> config, DateTime sinceUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<MarketplaceOrderDto>>([]);
    public Task<MarketplaceSyncResult> ReportShipmentAsync(IReadOnlyDictionary<string, string> config, string marketplaceOrderId, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
}

public sealed class AmazonConnector() : ConnectorBase("amazon", "Amazon", [
    new ConfigFieldDefinition("SellerId", "Satıcı No", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("RefreshToken", "Refresh Token", ConfigFieldType.Password, true),
    new ConfigFieldDefinition("ClientId", "Client Id (LWA)", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("ClientSecret", "Client Secret (LWA)", ConfigFieldType.Password, true)]), IMarketplaceConnector
{
    public Task<MarketplaceSyncResult> PushListingAsync(IReadOnlyDictionary<string, string> config, Guid productId, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushStockAsync(IReadOnlyDictionary<string, string> config, Guid productId, int quantity, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<MarketplaceSyncResult> PushPriceAsync(IReadOnlyDictionary<string, string> config, Guid productId, decimal priceTry, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
    public Task<IReadOnlyCollection<MarketplaceOrderDto>> PullOrdersAsync(IReadOnlyDictionary<string, string> config, DateTime sinceUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<MarketplaceOrderDto>>([]);
    public Task<MarketplaceSyncResult> ReportShipmentAsync(IReadOnlyDictionary<string, string> config, string marketplaceOrderId, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new MarketplaceSyncResult(true, null));
}
