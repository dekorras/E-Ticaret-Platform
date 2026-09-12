using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record RemoveCartItemCommand(string SessionKey, Guid ProductId, Guid? VariantId = null) : IRequest<Unit>;

public sealed class RemoveCartItemCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveCartItemCommand, Unit>
{
    public async Task<Unit> Handle(RemoveCartItemCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Cart>();
        var cart = repository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null) return Unit.Value;

        await repository.LoadCollectionAsync(cart, c => c.Items, cancellationToken);

        // Update(cart) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        cart.RemoveItem(request.ProductId, request.VariantId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
