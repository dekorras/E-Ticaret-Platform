using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

/// <summary>Sipariş kargoya verilirken takip numarasını kaydeder ve siparişi Kargoya Verildi
/// durumuna geçirir (bkz. Domain.Order.AttachTrackingNumber, plan §9.1 "kargoya verilen bir
/// siparişin takip numarasının ilgili pazaryerine geri bildirilmesi" akışının yerel kaydı).</summary>
public sealed record AttachTrackingNumberCommand(Guid OrderId, string TrackingNumber) : IRequest<Unit>;

public sealed class AttachTrackingNumberCommandValidator : AbstractValidator<AttachTrackingNumberCommand>
{
    public AttachTrackingNumberCommandValidator()
    {
        RuleFor(x => x.TrackingNumber).NotEmpty();
    }
}

public sealed class AttachTrackingNumberCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AttachTrackingNumberCommand, Unit>
{
    public async Task<Unit> Handle(AttachTrackingNumberCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Order>();
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.OrderId}' numaralı sipariş bulunamadı.");

        // Update(order) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        order.AttachTrackingNumber(request.TrackingNumber);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
