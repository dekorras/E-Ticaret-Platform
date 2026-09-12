using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record LedgerTransactionDto(
    Guid Id,
    LedgerTransactionDirection Direction,
    decimal AmountTry,
    string Description,
    Guid? RelatedOrderId,
    DateTime TransactionDateUtc);

public sealed record LedgerAccountDetailDto(
    Guid Id,
    string Name,
    LedgerAccountType Type,
    string? TaxOffice,
    string? TaxNumber,
    string? Phone,
    Guid? LinkedCustomerId,
    decimal OpenBalanceTry,
    decimal CheckNoteBalanceTry,
    bool IsActive,
    IReadOnlyCollection<LedgerTransactionDto> Transactions);

public sealed record GetLedgerAccountDetailQuery(Guid Id) : IRequest<LedgerAccountDetailDto?>;

public sealed class GetLedgerAccountDetailQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetLedgerAccountDetailQuery, LedgerAccountDetailDto?>
{
    public Task<LedgerAccountDetailDto?> Handle(GetLedgerAccountDetailQuery request, CancellationToken cancellationToken)
    {
        var dto = unitOfWork.Repository<LedgerAccount>().Query()
            .Where(a => a.Id == request.Id)
            .Select(a => new LedgerAccountDetailDto(
                a.Id, a.Name, a.Type, a.TaxOffice, a.TaxNumber, a.Phone, a.LinkedCustomerId,
                a.OpenBalanceTry, a.CheckNoteBalanceTry, a.IsActive,
                a.Transactions
                    .OrderByDescending(t => t.TransactionDateUtc)
                    .Select(t => new LedgerTransactionDto(t.Id, t.Direction, t.AmountTry, t.Description, t.RelatedOrderId, t.TransactionDateUtc))
                    .ToList()))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}
