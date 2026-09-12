using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;
using MediatR;

namespace Dekorras.Application.Integrations.Commands;

/// <summary>Zaten yapılandırılmış bir sağlayıcının bağlantısını, yeniden tüm alanları doldurmaya
/// GEREK KALMADAN, kayıtlı (şifresi çözülmüş) değerlerle yeniden test eder - önceden yalnızca
/// `ConfigureIntegrationProviderCommand` (kaydetme anında) bir test yapıyordu, sağlayıcı zamanla
/// bozulursa (ör. sağlayıcı tarafında bir API anahtarı iptal edilirse) bunu fark etmenin tüm formu
/// yeniden doldurup kaydetmekten başka bir yolu yoktu.</summary>
public sealed record TestIntegrationProviderConnectionCommand(Guid ProviderId) : IRequest<ConfigureIntegrationProviderResult>;

public sealed class TestIntegrationProviderConnectionCommandHandler(
    IProviderRegistry registry,
    IUnitOfWork unitOfWork,
    ISecretProtector secretProtector)
    : IRequestHandler<TestIntegrationProviderConnectionCommand, ConfigureIntegrationProviderResult>
{
    public async Task<ConfigureIntegrationProviderResult> Handle(TestIntegrationProviderConnectionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<IntegrationProvider>();
        var provider = await repository.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProviderId}' numaralı sağlayıcı bulunamadı.");

        await repository.LoadCollectionAsync(provider, p => p.ConfigFields, cancellationToken);

        var connector = registry.GetByKey(provider.ProviderKey)
            ?? throw new InvalidOperationException($"'{provider.ProviderKey}' anahtarlı bir connector bulunamadı.");

        var configValues = provider.ConfigFields.ToDictionary(f => f.FieldKey, f => secretProtector.Unprotect(f.EncryptedValue));

        var healthCheck = await connector.TestConnectionAsync(configValues, cancellationToken);
        provider.RecordHealthCheck(healthCheck.Success, healthCheck.Message);
        await unitOfWork.Repository<IntegrationHealthCheckLog>().AddAsync(
            new IntegrationHealthCheckLog(provider.Id, healthCheck.Success, healthCheck.Message), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ConfigureIntegrationProviderResult(healthCheck.Success, provider.Status, healthCheck.Message);
    }
}
