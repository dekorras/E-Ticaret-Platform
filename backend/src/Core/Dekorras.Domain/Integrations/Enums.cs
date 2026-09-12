namespace Dekorras.Domain.Integrations;

public enum ProviderCategory
{
    Payment,
    Cargo,
    Marketplace,
    EInvoice
}

public enum ProviderStatus
{
    NotConfigured, // Yapılandırılmadı
    Active,        // Aktif
    Error          // Hata
}
