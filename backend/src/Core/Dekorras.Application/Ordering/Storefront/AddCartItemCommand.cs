using Dekorras.Application.Catalog;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record AddCartItemCommand(string SessionKey, Guid ProductId, int Quantity, Guid? VariantId = null, Guid? CustomerGroupId = null, Guid? CustomerId = null) : IRequest<Unit>;

public sealed class AddCartItemCommandValidator : AbstractValidator<AddCartItemCommand>
{
    public AddCartItemCommandValidator()
    {
        RuleFor(x => x.SessionKey).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class AddCartItemCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddCartItemCommand, Unit>
{
    public async Task<Unit> Handle(AddCartItemCommand request, CancellationToken cancellationToken)
    {
        var product = await unitOfWork.Repository<Product>().GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        if (product.Status != ProductStatus.Active)
            throw new InvalidOperationException("Bu ürün şu anda satışa kapalıdır.");

        var quantity = Math.Max(request.Quantity, product.MinimumOrderQuantity);

        var unitPriceTry = ProductPricingHelper.ResolveUnitPriceTry(unitOfWork, product.Id, product.BasePriceTry, quantity, request.CustomerGroupId);

        if (request.VariantId is Guid variantId)
        {
            var variant = unitOfWork.Repository<ProductVariant>().Query().FirstOrDefault(v => v.Id == variantId)
                ?? throw new KeyNotFoundException($"'{variantId}' numaralı varyant bulunamadı.");
            if (variant.ProductId != product.Id)
                throw new InvalidOperationException("Seçilen varyant bu ürüne ait değil.");

            unitPriceTry += variant.PriceAdjustmentTry ?? 0m;
        }

        // Update() hiçbir dalda BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki
        // not. Burada özellikle önemlidir: yeni sepet dalında AddAsync zaten "Added" izliyor;
        // var olan sepet dalında ise AddOrUpdateItem yeni bir CartItem ekleyebilir - iki durumda
        // da Update() çağrısı bu yeni çocuğu "Modified" sanıp DbUpdateConcurrencyException fırlatırdı.
        var cartRepository = unitOfWork.Repository<Cart>();
        var cart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null)
        {
            cart = new Cart(request.SessionKey, request.CustomerId);
            cart.AddOrUpdateItem(product.Id, request.VariantId, quantity, unitPriceTry);
            await cartRepository.AddAsync(cart, cancellationToken);
        }
        else
        {
            // Items yüklenmeden AddOrUpdateItem çağrılırsa mevcut satır bulunamaz sanılır ve
            // aynı ürün için yinelenen bir CartItem eklenir - bkz. IRepository<T>.LoadCollectionAsync.
            await cartRepository.LoadCollectionAsync(cart, c => c.Items, cancellationToken);
            cart.AddOrUpdateItem(product.Id, request.VariantId, quantity, unitPriceTry);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
