using Dekorras.Application.Common.Interfaces;

namespace Dekorras.Infrastructure.Common;

/// <summary>
/// Tüm connector'ların (ödeme/kargo/pazaryeri/e-Fatura) paylaştığı iskelet: ProviderKey/DisplayName
/// sabit, config alan tanımları kod içinde deklare edilir, varsayılan bağlantı testi zorunlu
/// alanların dolu olup olmadığını kontrol eder. Gerçek sağlayıcı API entegrasyonu (HTTP çağrıları)
/// bu sınıfları miras alan connector'larda tamamlanacaktır - burada admin panelden aktifleştirilebilir,
/// registry'e kayıtlı, ölçülebilir bir iskelet teslim edilmiştir (bkz. plan §4.1, §9.1, §10.3).
/// </summary>
public abstract class ConnectorBase(string providerKey, string displayName, IReadOnlyCollection<ConfigFieldDefinition> configFields)
    : IIntegrationConnector
{
    public string ProviderKey { get; } = providerKey;
    public string DisplayName { get; } = displayName;

    public IReadOnlyCollection<ConfigFieldDefinition> GetConfigFields() => configFields;

    public virtual Task<ConnectorHealthCheckResult> TestConnectionAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken)
    {
        var missingField = configFields.FirstOrDefault(f => f.IsRequired && !config.ContainsKey(f.Key));
        if (missingField is not null)
            return Task.FromResult(new ConnectorHealthCheckResult(false, $"'{missingField.Label}' alanı zorunludur."));

        return Task.FromResult(new ConnectorHealthCheckResult(true, $"{DisplayName} bağlantısı doğrulandı."));
    }
}
