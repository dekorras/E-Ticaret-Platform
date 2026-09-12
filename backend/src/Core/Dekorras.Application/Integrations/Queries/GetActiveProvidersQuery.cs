using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;
using MediatR;

namespace Dekorras.Application.Integrations.Queries;

/// <summary>Checkout ekranının "hangi ödeme sağlayıcıları arasından seçim yapabilirim" sorusuna cevabı;
/// aynı sorgu kargo/pazaryeri/e-Fatura için de kullanılır. Yalnızca admin panelinden Aktif
/// duruma getirilmiş sağlayıcılar döner.</summary>
public sealed record GetActiveProvidersQuery(ProviderCategory Category) : IRequest<IReadOnlyCollection<ActiveProviderDto>>;

public sealed record ActiveProviderDto(string ProviderKey, string DisplayName, bool IsDefault = false);

public sealed class GetActiveProvidersQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetActiveProvidersQuery, IReadOnlyCollection<ActiveProviderDto>>
{
    public Task<IReadOnlyCollection<ActiveProviderDto>> Handle(GetActiveProvidersQuery request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<IntegrationProvider>();
        var result = repository.Query()
            .Where(p => p.Category == request.Category && p.Status == ProviderStatus.Active)
            .Select(p => new ActiveProviderDto(p.ProviderKey, p.DisplayName, p.IsDefaultForCategory))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ActiveProviderDto>>(result);
    }
}
