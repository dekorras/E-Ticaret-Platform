using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductVariantDto(Guid Id, string Sku, string OptionName, decimal? PriceAdjustmentTry, int StockQuantity);

public sealed record GetProductVariantsQuery(Guid ProductId) : IRequest<IReadOnlyCollection<ProductVariantDto>>;

public sealed class GetProductVariantsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductVariantsQuery, IReadOnlyCollection<ProductVariantDto>>
{
    public Task<IReadOnlyCollection<ProductVariantDto>> Handle(GetProductVariantsQuery request, CancellationToken cancellationToken)
    {
        var variants = unitOfWork.Repository<ProductVariant>().Query()
            .Where(v => v.ProductId == request.ProductId)
            .Select(v => new ProductVariantDto(v.Id, v.Sku, v.OptionName, v.PriceAdjustmentTry, v.StockQuantity))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductVariantDto>>(variants);
    }
}
