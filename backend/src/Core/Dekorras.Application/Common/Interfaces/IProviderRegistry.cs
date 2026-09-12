using Dekorras.Domain.Integrations;

namespace Dekorras.Application.Common.Interfaces;

/// <summary>
/// Uygulama başlangıcında (DI container kurulurken) TÜM connector sınıfları (ödeme, kargo,
/// pazaryeri, e-Fatura) burada kayıtlı olur - hiçbiri varsayılan olarak aktif değildir.
/// Hangisinin gerçekten kullanılacağı, admin panelden IntegrationProvider kaydı
/// oluşturulup Active durumuna getirilmesine bağlıdır (bkz. plan §4.1).
/// Yeni bir connector eklemek yalnızca yeni bir sınıf yazıp burada Register çağırmakla mümkündür -
/// Admin UI'da elle yeni bir ekran yazmaya gerek yoktur (form ConfigFieldDefinition'dan üretilir).
/// </summary>
public interface IProviderRegistry
{
    IReadOnlyCollection<IIntegrationConnector> GetAll(ProviderCategory category);
    IIntegrationConnector? GetByKey(string providerKey);
    IPaymentGateway GetPaymentGateway(string providerKey);
    ICargoProvider GetCargoProvider(string providerKey);
    IMarketplaceConnector GetMarketplaceConnector(string providerKey);
    IEInvoiceProvider GetEInvoiceProvider(string providerKey);
}
