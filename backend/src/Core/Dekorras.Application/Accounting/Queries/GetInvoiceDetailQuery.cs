using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record InvoiceLineDto(string Description, decimal UnitPriceTry, int Quantity, decimal TaxRatePercentage, decimal LineTotalTry, decimal LineTaxTry);
public sealed record EInvoiceLogDto(bool Success, string? GibDocumentNumber, string ProviderName, string? ResponseMessage, DateTime SentAtUtc);

public sealed record InvoiceDetailDto(
    Guid Id,
    string InvoiceNumber,
    Guid LedgerAccountId,
    string LedgerAccountName,
    InvoiceStatus Status,
    decimal SubTotalTry,
    decimal TaxTotalTry,
    decimal GrandTotalTry,
    bool IsExportInvoice,
    DateTime? IssuedAtUtc,
    DateTime CreatedAtUtc,
    string? OrderNumber,
    string? WaybillNumber,
    IReadOnlyCollection<InvoiceLineDto> Lines,
    IReadOnlyCollection<EInvoiceLogDto> EInvoiceLogs);

public sealed record GetInvoiceDetailQuery(Guid Id) : IRequest<InvoiceDetailDto?>;

public sealed class GetInvoiceDetailQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetInvoiceDetailQuery, InvoiceDetailDto?>
{
    public Task<InvoiceDetailDto?> Handle(GetInvoiceDetailQuery request, CancellationToken cancellationToken)
    {
        var invoice = unitOfWork.Repository<Invoice>().Query().FirstOrDefault(i => i.Id == request.Id);
        if (invoice is null) return Task.FromResult<InvoiceDetailDto?>(null);

        var ledgerAccountName = unitOfWork.Repository<LedgerAccount>().Query()
            .Where(a => a.Id == invoice.LedgerAccountId)
            .Select(a => a.Name)
            .FirstOrDefault() ?? "Bilinmeyen Cari";

        var orderNumber = invoice.OrderId is Guid orderId
            ? unitOfWork.Repository<Order>().Query().Where(o => o.Id == orderId).Select(o => o.OrderNumber).FirstOrDefault()
            : null;

        var waybillNumber = unitOfWork.Repository<Waybill>().Query()
            .Where(w => w.InvoiceId == invoice.Id)
            .Select(w => w.WaybillNumber)
            .FirstOrDefault();

        var lines = unitOfWork.Repository<InvoiceLine>().Query()
            .Where(l => l.InvoiceId == invoice.Id)
            .Select(l => new InvoiceLineDto(l.Description, l.UnitPriceTry, l.Quantity, l.TaxRatePercentage, l.UnitPriceTry * l.Quantity, l.UnitPriceTry * l.Quantity * l.TaxRatePercentage / 100m))
            .ToList();

        var eInvoiceLogs = unitOfWork.Repository<EInvoiceLog>().Query()
            .Where(l => l.InvoiceId == invoice.Id)
            .OrderByDescending(l => l.SentAtUtc)
            .Join(unitOfWork.Repository<IntegrationProvider>().Query(),
                l => l.IntegrationProviderId,
                p => p.Id,
                (l, p) => new EInvoiceLogDto(l.Success, l.GibDocumentNumber, p.DisplayName, l.ResponseMessage, l.SentAtUtc))
            .ToList();

        var dto = new InvoiceDetailDto(
            invoice.Id, invoice.InvoiceNumber, invoice.LedgerAccountId, ledgerAccountName, invoice.Status,
            invoice.SubTotalTry, invoice.TaxTotalTry, invoice.GrandTotalTry, invoice.IsExportInvoice,
            invoice.IssuedAtUtc, invoice.CreatedAtUtc, orderNumber, waybillNumber, lines, eInvoiceLogs);

        return Task.FromResult<InvoiceDetailDto?>(dto);
    }
}
