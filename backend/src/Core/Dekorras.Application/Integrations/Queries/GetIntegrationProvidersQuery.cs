using Dekorras.Application.Common.Interfaces;
using Dekorras.Application.Integrations.Dtos;
using Dekorras.Domain.Integrations;
using MediatR;

namespace Dekorras.Application.Integrations.Queries;

/// <summary>Admin panelin "Entegrasyonlar" ekranındaki bir sekmeyi (Ödeme/Kargo/Pazaryeri/e-Fatura)
/// doldurur: kodda kayıtlı TÜM connector'ları, veritabanındaki yapılandırma durumlarıyla birleştirir.
/// Henüz hiç yapılandırılmamış bir connector da listede "Yapılandırılmadı" olarak görünür.</summary>
public sealed record GetIntegrationProvidersQuery(ProviderCategory Category) : IRequest<IReadOnlyCollection<IntegrationProviderDto>>;

public sealed class GetIntegrationProvidersQueryHandler(IProviderRegistry registry, IUnitOfWork unitOfWork)
    : IRequestHandler<GetIntegrationProvidersQuery, IReadOnlyCollection<IntegrationProviderDto>>
{
    public async Task<IReadOnlyCollection<IntegrationProviderDto>> Handle(GetIntegrationProvidersQuery request, CancellationToken cancellationToken)
    {
        var connectors = registry.GetAll(request.Category);
        var repository = unitOfWork.Repository<IntegrationProvider>();

        var existing = repository.Query()
            .Where(p => p.Category == request.Category)
            .ToDictionary(p => p.ProviderKey);

        var result = new List<IntegrationProviderDto>();
        foreach (var connector in connectors)
        {
            existing.TryGetValue(connector.ProviderKey, out var entity);
            result.Add(new IntegrationProviderDto(
                entity?.Id,
                connector.ProviderKey,
                request.Category,
                connector.DisplayName,
                entity?.Status ?? ProviderStatus.NotConfigured,
                entity?.LastHealthCheckAtUtc,
                entity?.LastHealthCheckMessage,
                connector.GetConfigFields(),
                entity?.IsDefaultForCategory ?? false));
        }

        return await Task.FromResult(result);
    }
}
