using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductListItemDto(Guid Id, string Slug, string ProductCode, string Name, decimal PriceTry, int StockQuantity, ProductStatus Status);

public sealed record GetProductsQuery(string LanguageCode, Guid? CategoryId, int Page = 1, int PageSize = 25)
    : IRequest<IReadOnlyCollection<ProductListItemDto>>;

public sealed class GetProductsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductsQuery, IReadOnlyCollection<ProductListItemDto>>
{
    public Task<IReadOnlyCollection<ProductListItemDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<Product>().Query();

        if (request.CategoryId is not null)
            query = query.Where(p => p.ProductCategories.Any(pc => pc.CategoryId == request.CategoryId));

        var result = query
            .OrderBy(p => p.DisplayOrder)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new ProductListItemDto(
                p.Id,
                p.Slug,
                p.ProductCode,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                p.BasePriceTry,
                p.StockQuantity,
                p.Status))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductListItemDto>>(result);
    }
}
