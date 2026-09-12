using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record BrandDto(Guid Id, string Name, string Slug, bool IsActive);

public sealed record GetBrandsQuery : IRequest<IReadOnlyCollection<BrandDto>>;

public sealed class GetBrandsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBrandsQuery, IReadOnlyCollection<BrandDto>>
{
    public Task<IReadOnlyCollection<BrandDto>> Handle(GetBrandsQuery request, CancellationToken cancellationToken)
    {
        var brands = unitOfWork.Repository<Brand>().Query()
            .OrderBy(b => b.Name)
            .Select(b => new BrandDto(b.Id, b.Name, b.Slug, b.IsActive))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<BrandDto>>(brands);
    }
}
