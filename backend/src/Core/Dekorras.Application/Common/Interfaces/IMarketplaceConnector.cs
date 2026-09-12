namespace Dekorras.Application.Common.Interfaces;

public sealed record MarketplaceOrderDto(string MarketplaceOrderId, string CustomerName, decimal TotalAmountTry, DateTime OrderedAtUtc);
public sealed record MarketplaceSyncResult(bool Success, string? Message);

/// <summary>Hazır pazaryeri sözleşmesi: Trendyol, Hepsiburada, N11, İdefix, Amazon.
/// Dekorras'ın kendi ürünlerini bu pazaryerlerinde SATICI olarak listelemesi içindir
/// (bkz. plan §9) - Dekorras'ın kendisinin çoklu satıcılı bir platforma dönüşmesinden farklıdır.</summary>
public interface IMarketplaceConnector : IIntegrationConnector
{
    Task<MarketplaceSyncResult> PushListingAsync(IReadOnlyDictionary<string, string> config, Guid productId, CancellationToken cancellationToken);
    Task<MarketplaceSyncResult> PushStockAsync(IReadOnlyDictionary<string, string> config, Guid productId, int quantity, CancellationToken cancellationToken);
    Task<MarketplaceSyncResult> PushPriceAsync(IReadOnlyDictionary<string, string> config, Guid productId, decimal priceTry, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MarketplaceOrderDto>> PullOrdersAsync(IReadOnlyDictionary<string, string> config, DateTime sinceUtc, CancellationToken cancellationToken);
    Task<MarketplaceSyncResult> ReportShipmentAsync(IReadOnlyDictionary<string, string> config, string marketplaceOrderId, string trackingNumber, CancellationToken cancellationToken);
}
