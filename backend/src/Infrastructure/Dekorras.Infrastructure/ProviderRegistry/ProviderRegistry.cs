using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;

namespace Dekorras.Infrastructure.ProviderRegistry;

/// <summary>
/// DI container'a kayıtlı TÜM connector'ları (IIntegrationConnector implementasyonlarının tamamı)
/// ProviderKey'e göre indeksler. Burada bulunmak bir connector'ın aktif olduğu anlamına gelmez -
/// aktivasyon veritabanındaki IntegrationProvider.Status alanına bağlıdır (bkz. plan §4.1).
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IIntegrationConnector> _connectorsByKey;
    private readonly IReadOnlyDictionary<ProviderCategory, List<IIntegrationConnector>> _connectorsByCategory;

    public ProviderRegistry(IEnumerable<IPaymentGateway> paymentGateways, IEnumerable<ICargoProvider> cargoProviders,
        IEnumerable<IMarketplaceConnector> marketplaceConnectors, IEnumerable<IEInvoiceProvider> eInvoiceProviders)
    {
        var all = paymentGateways.Cast<IIntegrationConnector>()
            .Concat(cargoProviders)
            .Concat(marketplaceConnectors)
            .Concat(eInvoiceProviders)
            .ToList();

        _connectorsByKey = all.ToDictionary(c => c.ProviderKey);

        _connectorsByCategory = new Dictionary<ProviderCategory, List<IIntegrationConnector>>
        {
            [ProviderCategory.Payment] = paymentGateways.Cast<IIntegrationConnector>().ToList(),
            [ProviderCategory.Cargo] = cargoProviders.Cast<IIntegrationConnector>().ToList(),
            [ProviderCategory.Marketplace] = marketplaceConnectors.Cast<IIntegrationConnector>().ToList(),
            [ProviderCategory.EInvoice] = eInvoiceProviders.Cast<IIntegrationConnector>().ToList(),
        };
    }

    public IReadOnlyCollection<IIntegrationConnector> GetAll(ProviderCategory category) =>
        _connectorsByCategory.TryGetValue(category, out var list) ? list.AsReadOnly() : [];

    public IIntegrationConnector? GetByKey(string providerKey) =>
        _connectorsByKey.GetValueOrDefault(providerKey);

    public IPaymentGateway GetPaymentGateway(string providerKey) =>
        (IPaymentGateway)(GetByKey(providerKey) ?? throw NotFound(providerKey));

    public ICargoProvider GetCargoProvider(string providerKey) =>
        (ICargoProvider)(GetByKey(providerKey) ?? throw NotFound(providerKey));

    public IMarketplaceConnector GetMarketplaceConnector(string providerKey) =>
        (IMarketplaceConnector)(GetByKey(providerKey) ?? throw NotFound(providerKey));

    public IEInvoiceProvider GetEInvoiceProvider(string providerKey) =>
        (IEInvoiceProvider)(GetByKey(providerKey) ?? throw NotFound(providerKey));

    private static InvalidOperationException NotFound(string providerKey) =>
        new($"'{providerKey}' anahtarlı bir connector registry'de bulunamadı.");
}
