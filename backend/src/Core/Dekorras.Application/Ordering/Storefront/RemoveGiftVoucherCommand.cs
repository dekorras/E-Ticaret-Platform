using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record RemoveGiftVoucherCommand(string SessionKey) : IRequest<Unit>;

public sealed class RemoveGiftVoucherCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveGiftVoucherCommand, Unit>
{
    public async Task<Unit> Handle(RemoveGiftVoucherCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Cart>();
        var cart = repository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null) return Unit.Value;

        cart.RemoveGiftVoucher();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
