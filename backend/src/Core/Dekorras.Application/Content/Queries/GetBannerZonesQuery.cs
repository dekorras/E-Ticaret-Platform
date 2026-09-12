using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using MediatR;

namespace Dekorras.Application.Content.Queries;

public sealed record BannerZoneDto(Guid Id, string Key, string Name, string? Description, bool IsActive);

/// <summary>Admin liste ekranı için tüm banner bölgelerini döner.</summary>
public sealed record GetBannerZonesQuery : IRequest<IReadOnlyCollection<BannerZoneDto>>;

public sealed class GetBannerZonesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBannerZonesQuery, IReadOnlyCollection<BannerZoneDto>>
{
    public Task<IReadOnlyCollection<BannerZoneDto>> Handle(GetBannerZonesQuery request, CancellationToken cancellationToken)
    {
        var zones = unitOfWork.Repository<BannerZone>().Query()
            .OrderBy(z => z.Name)
            .Select(z => new BannerZoneDto(z.Id, z.Key, z.Name, z.Description, z.IsActive))
            .ToList();

        return Task.FromResult<IReadOnlyCollection<BannerZoneDto>>(zones);
    }
}

public sealed record GetBannerZoneByIdQuery(Guid Id) : IRequest<BannerZoneDto?>;

public sealed class GetBannerZoneByIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBannerZoneByIdQuery, BannerZoneDto?>
{
    public Task<BannerZoneDto?> Handle(GetBannerZoneByIdQuery request, CancellationToken cancellationToken)
    {
        var zone = unitOfWork.Repository<BannerZone>().Query()
            .Where(z => z.Id == request.Id)
            .Select(z => new BannerZoneDto(z.Id, z.Key, z.Name, z.Description, z.IsActive))
            .FirstOrDefault();

        return Task.FromResult(zone);
    }
}
