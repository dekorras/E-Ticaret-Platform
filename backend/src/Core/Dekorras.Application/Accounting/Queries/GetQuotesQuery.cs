using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record QuoteListItemDto(Guid Id, string QuoteNumber, string LedgerAccountName, decimal TotalAmountTry, DateTime ValidUntilUtc, DateTime CreatedAtUtc, bool IsConverted);

public sealed record GetQuotesQuery : IRequest<IReadOnlyCollection<QuoteListItemDto>>;

public sealed class GetQuotesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetQuotesQuery, IReadOnlyCollection<QuoteListItemDto>>
{
    public Task<IReadOnlyCollection<QuoteListItemDto>> Handle(GetQuotesQuery request, CancellationToken cancellationToken)
    {
        var quotes = unitOfWork.Repository<Quote>().Query()
            .OrderByDescending(q => q.CreatedAtUtc)
            .Join(unitOfWork.Repository<LedgerAccount>().Query(),
                q => q.LedgerAccountId,
                a => a.Id,
                (q, a) => new QuoteListItemDto(q.Id, q.QuoteNumber, a.Name, q.TotalAmountTry, q.ValidUntilUtc, q.CreatedAtUtc, q.ConvertedToOrderId != null))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<QuoteListItemDto>>(quotes);
    }
}
