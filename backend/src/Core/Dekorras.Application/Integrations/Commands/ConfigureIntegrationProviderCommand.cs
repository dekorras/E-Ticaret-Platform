using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.SystemAdmin;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Integrations.Commands;

/// <summary>
/// Bir connector kartına admin panelinde tıklanıp form doldurulduğunda çalışır: değerleri
/// şifreleyip saklar, bağlantı testini (health check) dener, başarılıysa sağlayıcıyı Aktif yapar.
/// Bu andan itibaren connector, checkout/kargo hesaplama/pazaryeri senkronu/fatura kesme
/// akışlarında kullanılabilir hale gelir - kod değişikliği veya deploy GEREKMEZ (bkz. §4.1).
/// </summary>
public sealed record ConfigureIntegrationProviderCommand(
    string ProviderKey,
    ProviderCategory Category,
    IReadOnlyDictionary<string, string> ConfigValues,
    string? ActingIdentityUserId) : IRequest<ConfigureIntegrationProviderResult>;

public sealed record ConfigureIntegrationProviderResult(bool Success, ProviderStatus Status, string? Message);

public sealed class ConfigureIntegrationProviderCommandValidator : AbstractValidator<ConfigureIntegrationProviderCommand>
{
    public ConfigureIntegrationProviderCommandValidator()
    {
        RuleFor(x => x.ProviderKey).NotEmpty();
    }
}

public sealed class ConfigureIntegrationProviderCommandHandler(
    IProviderRegistry registry,
    IUnitOfWork unitOfWork,
    ISecretProtector secretProtector)
    : IRequestHandler<ConfigureIntegrationProviderCommand, ConfigureIntegrationProviderResult>
{
    public async Task<ConfigureIntegrationProviderResult> Handle(ConfigureIntegrationProviderCommand request, CancellationToken cancellationToken)
    {
        var connector = registry.GetByKey(request.ProviderKey)
            ?? throw new InvalidOperationException($"'{request.ProviderKey}' anahtarlı bir connector bulunamadı.");

        var repository = unitOfWork.Repository<IntegrationProvider>();
        var provider = repository.Query().FirstOrDefault(p => p.ProviderKey == request.ProviderKey);

        if (provider is null)
        {
            provider = new IntegrationProvider(request.ProviderKey, request.Category, connector.DisplayName);
            await repository.AddAsync(provider, cancellationToken);
        }
        else
        {
            // ConfigFields yüklenmeden SetConfigValue çağrılırsa mevcut alan bulunamaz sanılır ve
            // yinelenen bir satır eklenir - bkz. IRepository<T>.LoadCollectionAsync dokümantasyonu.
            await repository.LoadCollectionAsync(provider, p => p.ConfigFields, cancellationToken);
        }

        foreach (var (key, value) in request.ConfigValues)
            provider.SetConfigValue(key, secretProtector.Protect(value));

        var healthCheck = await connector.TestConnectionAsync(request.ConfigValues, cancellationToken);
        provider.RecordHealthCheck(healthCheck.Success, healthCheck.Message);
        // `IntegrationHealthCheckLog` Faz 0/1'den beri EF'e kayıtlıydı ama hiçbir yerden hiç
        // eklenmiyordu - IntegrationProvider yalnızca SON kontrolü tutuyordu, geçmiş kontrollerin
        // hiçbir izi kalmıyordu (bkz. Entegrasyonlar sayfasındaki "Geçmiş" bölümü).
        await unitOfWork.Repository<IntegrationHealthCheckLog>().AddAsync(
            new IntegrationHealthCheckLog(provider.Id, healthCheck.Success, healthCheck.Message), cancellationToken);

        var auditLogRepository = unitOfWork.Repository<AuditLog>();
        await auditLogRepository.AddAsync(new AuditLog(
            request.ActingIdentityUserId,
            healthCheck.Success ? "IntegrationProvider.Activated" : "IntegrationProvider.ActivationFailed",
            nameof(IntegrationProvider),
            provider.Id.ToString(),
            null), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ConfigureIntegrationProviderResult(healthCheck.Success, provider.Status, healthCheck.Message);
    }
}
