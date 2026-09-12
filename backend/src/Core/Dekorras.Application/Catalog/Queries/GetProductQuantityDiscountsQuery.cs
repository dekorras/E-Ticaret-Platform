using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record QuantityDiscountDto(Guid Id, int MinimumQuantity, decimal PriceTry);

public sealed record GetProductQuantityDiscountsQuery(Guid ProductId) : IRequest<IReadOnlyCollection<QuantityDiscountDto>>;

public sealed class GetProductQuantityDiscountsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductQuantityDiscountsQuery, IReadOnlyCollection<QuantityDiscountDto>>
{
    public Task<IReadOnlyCollection<QuantityDiscountDto>> Handle(GetProductQuantityDiscountsQuery request, CancellationToken cancellationToken)
    {
        var discounts = unitOfWork.Repository<QuantityDiscount>().Query()
            .Where(d => d.ProductId == request.ProductId)
            .OrderBy(d => d.MinimumQuantity)
            .Select(d => new QuantityDiscountDto(d.Id, d.MinimumQuantity, d.PriceTry))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<QuantityDiscountDto>>(discounts);
    }
}
