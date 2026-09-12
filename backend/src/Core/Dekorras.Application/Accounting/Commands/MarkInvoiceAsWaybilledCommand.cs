using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

/// <summary>
/// Fatura "İrsaliyeleşmiş" durumuna geçerken gerçek bir Waybill (İrsaliye) kaydı da oluşturur -
/// önceden yalnızca Invoice.Status değişiyordu, ayrı bir Waybill entity'si (kendi numarasıyla)
/// Faz 0/1'den beri domain'de vardı ama hiç kullanılmıyordu.
/// </summary>
public sealed record MarkInvoiceAsWaybilledCommand(Guid InvoiceId) : IRequest<string>;

public sealed class MarkInvoiceAsWaybilledCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MarkInvoiceAsWaybilledCommand, string>
{
    public async Task<string> Handle(MarkInvoiceAsWaybilledCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Invoice>();
        var invoice = await repository.GetByIdAsync(request.InvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.InvoiceId}' numaralı fatura bulunamadı.");

        // Update(invoice) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        invoice.MarkAsWaybilled();

        var waybillNumber = $"IRS-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
        var waybill = new Waybill(waybillNumber, invoice.LedgerAccountId, invoice.OrderId, invoice.Id);
        await unitOfWork.Repository<Waybill>().AddAsync(waybill, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return waybillNumber;
    }
}
