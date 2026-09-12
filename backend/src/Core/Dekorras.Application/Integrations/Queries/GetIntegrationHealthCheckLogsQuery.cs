using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;
using MediatR;

namespace Dekorras.Application.Integrations.Queries;

public sealed record IntegrationHealthCheckLogDto(DateTime CheckedAtUtc, bool Success, string? Message);

public sealed record GetIntegrationHealthCheckLogsQuery(Guid ProviderId) : IRequest<IReadOnlyCollection<IntegrationHealthCheckLogDto>>;

public sealed class GetIntegrationHealthCheckLogsQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetIntegrationHealthCheckLogsQuery, IReadOnlyCollection<IntegrationHealthCheckLogDto>>
{
    public Task<IReadOnlyCollection<IntegrationHealthCheckLogDto>> Handle(GetIntegrationHealthCheckLogsQuery request, CancellationToken cancellationToken)
    {
        var logs = unitOfWork.Repository<IntegrationHealthCheckLog>().Query()
            .Where(l => l.IntegrationProviderId == request.ProviderId)
            .OrderByDescending(l => l.CheckedAtUtc)
            .Take(10)
            .Select(l => new IntegrationHealthCheckLogDto(l.CheckedAtUtc, l.Success, l.Message))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<IntegrationHealthCheckLogDto>>(logs);
    }
}
