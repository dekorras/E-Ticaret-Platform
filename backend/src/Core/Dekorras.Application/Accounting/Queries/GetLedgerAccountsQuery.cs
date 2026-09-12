using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record LedgerAccountListItemDto(
    Guid Id,
    string Name,
    LedgerAccountType Type,
    string? TaxNumber,
    decimal OpenBalanceTry,
    bool IsActive,
    int TransactionCount);

public sealed record GetLedgerAccountsQuery(string? SearchText = null) : IRequest<IReadOnlyCollection<LedgerAccountListItemDto>>;

public sealed class GetLedgerAccountsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetLedgerAccountsQuery, IReadOnlyCollection<LedgerAccountListItemDto>>
{
    public Task<IReadOnlyCollection<LedgerAccountListItemDto>> Handle(GetLedgerAccountsQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<LedgerAccount>().Query();

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.Trim();
            query = query.Where(a => a.Name.Contains(search) || (a.TaxNumber != null && a.TaxNumber.Contains(search)));
        }

        var accounts = query
            .OrderBy(a => a.Name)
            .Select(a => new LedgerAccountListItemDto(
                a.Id, a.Name, a.Type, a.TaxNumber, a.OpenBalanceTry, a.IsActive, a.Transactions.Count))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<LedgerAccountListItemDto>>(accounts);
    }
}
