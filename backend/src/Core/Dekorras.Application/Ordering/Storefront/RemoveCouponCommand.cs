using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record RemoveCouponCommand(string SessionKey) : IRequest<Unit>;

public sealed class RemoveCouponCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveCouponCommand, Unit>
{
    public async Task<Unit> Handle(RemoveCouponCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Cart>();
        var cart = repository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null) return Unit.Value;

        cart.RemoveCoupon();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
