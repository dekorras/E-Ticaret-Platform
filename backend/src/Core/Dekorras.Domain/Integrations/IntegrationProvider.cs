using Dekorras.Domain.Common;

namespace Dekorras.Domain.Integrations;

/// <summary>
/// Provider Registry'nin veritabanı karşılığı: her satır, kodda ProviderKey ile tanımlanmış
/// bir connector'ın (ör. "iyzico", "trendyol") admin panelinden girilmiş yapılandırmasını taşır.
/// Bir connector kodda yazılmış olsa bile burada bir kayıt oluşmadan/aktifleşmeden hiçbir akışta kullanılmaz.
/// </summary>
public class IntegrationProvider : AuditableEntity
{
    public string ProviderKey { get; private set; } = default!; // ör. "iyzico", "trendyol", "yurtici-kargo"
    public ProviderCategory Category { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public ProviderStatus Status { get; private set; } = ProviderStatus.NotConfigured;
    public DateTime? LastHealthCheckAtUtc { get; private set; }
    public string? LastHealthCheckMessage { get; private set; }
    public bool IsDefaultForCategory { get; private set; } // pazaryeri/kargo/e-fatura için tek aktif seçim senaryosunda

    private readonly List<IntegrationConfigField> _configFields = [];
    public IReadOnlyCollection<IntegrationConfigField> ConfigFields => _configFields.AsReadOnly();

    private IntegrationProvider() { }

    public IntegrationProvider(string providerKey, ProviderCategory category, string displayName)
    {
        ProviderKey = providerKey;
        Category = category;
        DisplayName = displayName;
    }

    public void SetConfigValue(string fieldKey, string encryptedValue)
    {
        var existing = _configFields.FirstOrDefault(f => f.FieldKey == fieldKey);
        if (existing is not null)
        {
            existing.SetValue(encryptedValue);
            return;
        }
        _configFields.Add(new IntegrationConfigField(Id, fieldKey, encryptedValue));
    }

    public void RecordHealthCheck(bool success, string? message)
    {
        LastHealthCheckAtUtc = DateTime.UtcNow;
        LastHealthCheckMessage = message;
        Status = success ? ProviderStatus.Active : ProviderStatus.Error;
    }

    public void Deactivate() => Status = ProviderStatus.NotConfigured;

    public void MarkAsDefaultForCategory() => IsDefaultForCategory = true;
    public void UnmarkAsDefaultForCategory() => IsDefaultForCategory = false;
}

public class IntegrationConfigField : BaseEntity
{
    public Guid IntegrationProviderId { get; private set; }
    public string FieldKey { get; private set; } = default!; // ör. "ApiKey", "SupplierId"
    public string EncryptedValue { get; private set; } = default!; // Data Protection API ile şifrelenmiş

    private IntegrationConfigField() { }

    public IntegrationConfigField(Guid integrationProviderId, string fieldKey, string encryptedValue)
    {
        IntegrationProviderId = integrationProviderId;
        FieldKey = fieldKey;
        EncryptedValue = encryptedValue;
    }

    public void SetValue(string encryptedValue) => EncryptedValue = encryptedValue;
}

public class IntegrationHealthCheckLog : BaseEntity
{
    public Guid IntegrationProviderId { get; private set; }
    public DateTime CheckedAtUtc { get; private set; } = DateTime.UtcNow;
    public bool Success { get; private set; }
    public string? Message { get; private set; }

    private IntegrationHealthCheckLog() { }

    public IntegrationHealthCheckLog(Guid integrationProviderId, bool success, string? message)
    {
        IntegrationProviderId = integrationProviderId;
        Success = success;
        Message = message;
    }
}
