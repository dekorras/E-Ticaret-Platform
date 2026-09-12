using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record InvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    string LedgerAccountName,
    InvoiceStatus Status,
    decimal GrandTotalTry,
    DateTime CreatedAtUtc,
    string? OrderNumber);

public sealed record GetInvoicesQuery(InvoiceStatus? StatusFilter = null) : IRequest<IReadOnlyCollection<InvoiceListItemDto>>;

public sealed class GetInvoicesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetInvoicesQuery, IReadOnlyCollection<InvoiceListItemDto>>
{
    public Task<IReadOnlyCollection<InvoiceListItemDto>> Handle(GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<Invoice>().Query();
        if (request.StatusFilter is InvoiceStatus status)
            query = query.Where(i => i.Status == status);

        var invoices = query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Join(unitOfWork.Repository<LedgerAccount>().Query(),
                i => i.LedgerAccountId,
                a => a.Id,
                (i, a) => new { Invoice = i, LedgerAccountName = a.Name })
            .ToList();

        var orderNumbersByOrderId = unitOfWork.Repository<Order>().Query()
            .Select(o => new { o.Id, o.OrderNumber })
            .ToDictionary(o => o.Id, o => o.OrderNumber);

        var result = invoices
            .Select(x => new InvoiceListItemDto(
                x.Invoice.Id,
                x.Invoice.InvoiceNumber,
                x.LedgerAccountName,
                x.Invoice.Status,
                x.Invoice.GrandTotalTry,
                x.Invoice.CreatedAtUtc,
                x.Invoice.OrderId is Guid orderId ? orderNumbersByOrderId.GetValueOrDefault(orderId) : null))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<InvoiceListItemDto>>(result);
    }
}
