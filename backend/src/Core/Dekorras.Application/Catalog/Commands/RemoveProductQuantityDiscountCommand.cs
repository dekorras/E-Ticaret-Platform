using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record RemoveProductQuantityDiscountCommand(Guid ProductId, Guid QuantityDiscountId) : IRequest<Unit>;

public sealed class RemoveProductQuantityDiscountCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveProductQuantityDiscountCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProductQuantityDiscountCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.QuantityDiscounts, cancellationToken);

        product.RemoveQuantityDiscount(request.QuantityDiscountId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
