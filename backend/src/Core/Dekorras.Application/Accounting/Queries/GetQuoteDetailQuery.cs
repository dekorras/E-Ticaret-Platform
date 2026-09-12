using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record QuoteLineDto(string Description, decimal UnitPriceTry, int Quantity, decimal LineTotalTry);

public sealed record QuoteDetailDto(
    Guid Id,
    string QuoteNumber,
    Guid LedgerAccountId,
    string LedgerAccountName,
    DateTime ValidUntilUtc,
    decimal TotalAmountTry,
    DateTime CreatedAtUtc,
    bool IsConverted,
    IReadOnlyCollection<QuoteLineDto> Lines);

public sealed record GetQuoteDetailQuery(Guid Id) : IRequest<QuoteDetailDto?>;

public sealed class GetQuoteDetailQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetQuoteDetailQuery, QuoteDetailDto?>
{
    public Task<QuoteDetailDto?> Handle(GetQuoteDetailQuery request, CancellationToken cancellationToken)
    {
        var quote = unitOfWork.Repository<Quote>().Query().FirstOrDefault(q => q.Id == request.Id);
        if (quote is null) return Task.FromResult<QuoteDetailDto?>(null);

        var ledgerAccountName = unitOfWork.Repository<LedgerAccount>().Query()
            .Where(a => a.Id == quote.LedgerAccountId)
            .Select(a => a.Name)
            .FirstOrDefault() ?? "Bilinmeyen Cari";

        var lines = unitOfWork.Repository<QuoteLine>().Query()
            .Where(l => l.QuoteId == quote.Id)
            .Select(l => new QuoteLineDto(l.Description, l.UnitPriceTry, l.Quantity, l.UnitPriceTry * l.Quantity))
            .ToList();

        var dto = new QuoteDetailDto(
            quote.Id, quote.QuoteNumber, quote.LedgerAccountId, ledgerAccountName,
            quote.ValidUntilUtc, quote.TotalAmountTry, quote.CreatedAtUtc, quote.ConvertedToOrderId != null, lines);

        return Task.FromResult<QuoteDetailDto?>(dto);
    }
}
