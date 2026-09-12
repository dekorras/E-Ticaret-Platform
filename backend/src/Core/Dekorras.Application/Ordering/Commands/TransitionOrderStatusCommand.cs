using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

/// <summary>Kabul kriteri: sipariş durum geçişleri 14 durumlu bir state machine ile kısıtlanmalı,
/// kaynağı (Web/Mobil/pazaryeri) fark etmeksizin - bkz. Domain.Ordering.OrderStatusTransitionRules.</summary>
public sealed record TransitionOrderStatusCommand(Guid OrderId, OrderStatus NewStatus, string? Note) : IRequest<Unit>;

public sealed class TransitionOrderStatusCommandValidator : AbstractValidator<TransitionOrderStatusCommand>
{
    public TransitionOrderStatusCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class TransitionOrderStatusCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<TransitionOrderStatusCommand, Unit>
{
    public async Task<Unit> Handle(TransitionOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Order>();
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.OrderId}' numaralı sipariş bulunamadı.");

        order.TransitionTo(request.NewStatus, request.Note);
        // Update(order) BİLİNÇLİ OLARAK çağrılmaz: order zaten bu DbContext tarafından izleniyor
        // ve TransitionTo yeni bir OrderStatusHistory ekliyor. Update() burada çağrılsaydı, EF
        // Core'un genel graph-walk sezgisi bu YENİ çocuğu "Added" yerine "Modified" işaretleyip
        // var olmayan bir satırı UPDATE etmeye çalışarak DbUpdateConcurrencyException fırlatırdı
        // (bu, kapsamlı bir entegrasyon testiyle yakalanıp düzeltilmiştir). EF Core'un otomatik
        // değişiklik algılaması (SaveChangesAsync içinde) zaten hem Status alanını hem de yeni
        // eklenen geçmiş kaydını doğru şekilde saptar.
        //
        // DomainEvents (ör. OrderCompletedEvent), DbContext.SaveChangesAsync sonrasında
        // IDomainEventDispatcher tarafından otomatik yayınlanır - bkz. Dekorras.Persistence.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
