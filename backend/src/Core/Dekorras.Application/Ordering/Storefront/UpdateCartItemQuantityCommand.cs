using Dekorras.Application.Catalog;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record UpdateCartItemQuantityCommand(string SessionKey, Guid ProductId, int Quantity, Guid? VariantId = null, Guid? CustomerGroupId = null) : IRequest<Unit>;

public sealed class UpdateCartItemQuantityCommandValidator : AbstractValidator<UpdateCartItemQuantityCommand>
{
    public UpdateCartItemQuantityCommandValidator()
    {
        RuleFor(x => x.SessionKey).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class UpdateCartItemQuantityCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateCartItemQuantityCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCartItemQuantityCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Cart>();
        var cart = repository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null) return Unit.Value;

        await repository.LoadCollectionAsync(cart, c => c.Items, cancellationToken);

        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId && i.VariantId == request.VariantId);
        if (existingItem is null) return Unit.Value;

        var product = await unitOfWork.Repository<Product>().GetByIdAsync(request.ProductId, cancellationToken);
        var unitPriceTry = existingItem.UnitPriceTry;
        if (product is not null)
        {
            // Adet değiştiğinde toplu alım kademesi (bkz. Domain.Catalog.QuantityDiscount)
            // değişmiş olabilir - fiyat her zaman YENİ adede göre yeniden hesaplanır.
            unitPriceTry = ProductPricingHelper.ResolveUnitPriceTry(unitOfWork, product.Id, product.BasePriceTry, request.Quantity, request.CustomerGroupId);

            if (request.VariantId is Guid variantId)
            {
                var variant = unitOfWork.Repository<ProductVariant>().Query().FirstOrDefault(v => v.Id == variantId);
                if (variant is not null)
                    unitPriceTry += variant.PriceAdjustmentTry ?? 0m;
            }
        }

        // Update(cart) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        cart.AddOrUpdateItem(request.ProductId, request.VariantId, request.Quantity, unitPriceTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
