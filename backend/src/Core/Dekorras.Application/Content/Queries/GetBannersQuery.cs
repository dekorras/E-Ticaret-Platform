using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using MediatR;

namespace Dekorras.Application.Content.Queries;

public sealed record BannerDto(Guid Id, string ImageUrl, string? LinkUrl, int DisplayOrder, bool IsActive);

/// <summary>Admin listesi - TÜM banner'ları (aktif/pasif) döner.</summary>
public sealed record GetBannersQuery : IRequest<IReadOnlyCollection<BannerDto>>;

public sealed class GetBannersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBannersQuery, IReadOnlyCollection<BannerDto>>
{
    public Task<IReadOnlyCollection<BannerDto>> Handle(GetBannersQuery request, CancellationToken cancellationToken)
    {
        var banners = unitOfWork.Repository<Banner>().Query()
            .OrderBy(b => b.DisplayOrder)
            .Select(b => new BannerDto(b.Id, b.ImageUrl, b.LinkUrl, b.DisplayOrder, b.IsActive))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<BannerDto>>(banners);
    }
}

/// <summary>Storefront - yalnızca Aktif banner'ları, gösterim sırasına göre döner.</summary>
public sealed record GetActiveBannersQuery : IRequest<IReadOnlyCollection<BannerDto>>;

public sealed class GetActiveBannersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetActiveBannersQuery, IReadOnlyCollection<BannerDto>>
{
    public Task<IReadOnlyCollection<BannerDto>> Handle(GetActiveBannersQuery request, CancellationToken cancellationToken)
    {
        var banners = unitOfWork.Repository<Banner>().Query()
            .Where(b => b.IsActive)
            .OrderBy(b => b.DisplayOrder)
            .Select(b => new BannerDto(b.Id, b.ImageUrl, b.LinkUrl, b.DisplayOrder, b.IsActive))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<BannerDto>>(banners);
    }
}
