using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.Payments;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

/// <summary>Sipariş "İade Edildi" durumuna geçerken GERÇEKTEN ödeme sağlayıcısına bir iade isteği
/// gönderir (`IPaymentGateway.RefundAsync`) ve sonucu `Payment`e bir `Refund` işlemi olarak kaydeder -
/// bu komut olmadan `TransitionOrderStatusCommand` yalnızca durum ETİKETİNİ değiştirirdi, para
/// hiçbir yerde GERÇEKTEN iade edilmiş olmazdı (bkz. plan §4.1 Provider Registry).</summary>
public sealed record RefundOrderCommand(Guid OrderId, string? Note) : IRequest<Unit>;

public sealed class RefundOrderCommandValidator : AbstractValidator<RefundOrderCommand>
{
    public RefundOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class RefundOrderCommandHandler(IUnitOfWork unitOfWork, IProviderRegistry providerRegistry, ISecretProtector secretProtector)
    : IRequestHandler<RefundOrderCommand, Unit>
{
    public async Task<Unit> Handle(RefundOrderCommand request, CancellationToken cancellationToken)
    {
        var orderRepository = unitOfWork.Repository<Order>();
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.OrderId}' numaralı sipariş bulunamadı.");

        var paymentRepository = unitOfWork.Repository<Payment>();
        var payment = paymentRepository.Query().FirstOrDefault(p => p.OrderId == request.OrderId)
            ?? throw new InvalidOperationException("Bu siparişe ait bir ödeme kaydı bulunamadı.");

        if (payment.Status is not (PaymentStatus.Authorized or PaymentStatus.Captured))
            throw new InvalidOperationException($"Bu ödeme '{payment.Status}' durumunda - yalnızca onaylanmış/tahsil edilmiş bir ödeme iade edilebilir.");

        await paymentRepository.LoadCollectionAsync(payment, p => p.Transactions, cancellationToken);
        var originalTransaction = payment.Transactions
            .Where(t => t.IsSuccess && t.Type is TransactionType.Authorization or TransactionType.Capture)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("İade edilecek başarılı bir ödeme işlemi bulunamadı.");

        var providerRepository = unitOfWork.Repository<IntegrationProvider>();
        var provider = await providerRepository.GetByIdAsync(payment.IntegrationProviderId, cancellationToken)
            ?? throw new InvalidOperationException("Ödeme sağlayıcısı bulunamadı.");
        await providerRepository.LoadCollectionAsync(provider, p => p.ConfigFields, cancellationToken);
        var config = provider.ConfigFields.ToDictionary(f => f.FieldKey, f => secretProtector.Unprotect(f.EncryptedValue));

        var paymentGateway = providerRegistry.GetPaymentGateway(provider.ProviderKey);
        var refundResult = await paymentGateway.RefundAsync(config, originalTransaction.ProviderTransactionReference ?? "", payment.AmountTry, cancellationToken);

        // Update(payment) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        payment.RecordTransaction(TransactionType.Refund, payment.AmountTry, refundResult.ProviderTransactionReference, refundResult.Success);

        if (!refundResult.Success)
        {
            // Başarısız girişim de denetim izi olarak KAYDEDİLİR - sipariş durumu DEĞİŞMEZ, admin
            // sorunu görüp tekrar deneyebilsin.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException($"İade başarısız: {refundResult.FailureReason ?? "bilinmeyen hata"}");
        }

        order.TransitionTo(OrderStatus.Refunded, request.Note);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
