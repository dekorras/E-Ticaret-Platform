using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record AddProductQuantityDiscountCommand(Guid ProductId, int MinimumQuantity, decimal PriceTry) : IRequest<Guid>;

public sealed class AddProductQuantityDiscountCommandValidator : AbstractValidator<AddProductQuantityDiscountCommand>
{
    public AddProductQuantityDiscountCommandValidator()
    {
        RuleFor(x => x.MinimumQuantity).GreaterThan(1).WithMessage("Toplu alım kademesi en az 2 adetten başlamalıdır.");
        RuleFor(x => x.PriceTry).GreaterThanOrEqualTo(0);
    }
}

public sealed class AddProductQuantityDiscountCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddProductQuantityDiscountCommand, Guid>
{
    public async Task<Guid> Handle(AddProductQuantityDiscountCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.QuantityDiscounts, cancellationToken);

        product.AddQuantityDiscount(request.MinimumQuantity, request.PriceTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return product.QuantityDiscounts.Last().Id;
    }
}
