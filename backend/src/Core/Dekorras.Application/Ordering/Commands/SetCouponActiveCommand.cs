using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

public sealed record SetCouponActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetCouponActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCouponActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetCouponActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Coupon>();
        var coupon = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı kupon bulunamadı.");

        // Update(coupon) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.IsActive) coupon.Activate(); else coupon.Deactivate();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
