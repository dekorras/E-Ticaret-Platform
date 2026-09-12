namespace Dekorras.Application.Common.Interfaces;

public sealed record ConnectorHealthCheckResult(bool Success, string? Message);

/// <summary>
/// Ödeme, kargo, pazaryeri ve e-Fatura connector'larının ortak sözleşmesi.
/// Her connector kendi ProviderKey'i ile Infrastructure katmanında bir sınıf olarak yazılır
/// ve uygulama başlarken ProviderRegistry'e kaydedilir (bkz. IProviderRegistry). Registry'e
/// kayıtlı olmak connector'ın AKTİF olduğu anlamına gelmez - aktivasyon admin panelinden,
/// IntegrationProvider durumunun Active olmasıyla gerçekleşir.
/// </summary>
public interface IIntegrationConnector
{
    string ProviderKey { get; }
    string DisplayName { get; }
    IReadOnlyCollection<ConfigFieldDefinition> GetConfigFields();
    Task<ConnectorHealthCheckResult> TestConnectionAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken);
}
