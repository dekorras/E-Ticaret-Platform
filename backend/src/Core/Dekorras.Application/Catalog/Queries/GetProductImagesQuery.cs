using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductImageDto(Guid Id, string Url, int DisplayOrder, bool IsPrimary);

public sealed record GetProductImagesQuery(Guid ProductId) : IRequest<IReadOnlyCollection<ProductImageDto>>;

public sealed class GetProductImagesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductImagesQuery, IReadOnlyCollection<ProductImageDto>>
{
    public Task<IReadOnlyCollection<ProductImageDto>> Handle(GetProductImagesQuery request, CancellationToken cancellationToken)
    {
        var images = unitOfWork.Repository<ProductImage>().Query()
            .Where(i => i.ProductId == request.ProductId)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new ProductImageDto(i.Id, i.Url, i.DisplayOrder, i.IsPrimary))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductImageDto>>(images);
    }
}
