using Dekorras.Domain.Common;

namespace Dekorras.Domain.Accounting;

public class Quote : AuditableEntity
{
    public string QuoteNumber { get; private set; } = default!;
    public Guid LedgerAccountId { get; private set; }
    public DateTime ValidUntilUtc { get; private set; }
    public decimal TotalAmountTry { get; private set; }
    public Guid? ConvertedToOrderId { get; private set; }

    private readonly List<QuoteLine> _lines = [];
    public IReadOnlyCollection<QuoteLine> Lines => _lines.AsReadOnly();

    private Quote() { }

    public Quote(string quoteNumber, Guid ledgerAccountId, DateTime validUntilUtc)
    {
        QuoteNumber = quoteNumber;
        LedgerAccountId = ledgerAccountId;
        ValidUntilUtc = validUntilUtc;
    }

    public void AddLine(string description, decimal unitPriceTry, int quantity)
    {
        _lines.Add(new QuoteLine(Id, description, unitPriceTry, quantity));
        TotalAmountTry = _lines.Sum(l => l.LineTotalTry);
    }

    public void ConvertToOrder(Guid orderId) => ConvertedToOrderId = orderId;
}

public class QuoteLine : BaseEntity
{
    public Guid QuoteId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal UnitPriceTry { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotalTry => UnitPriceTry * Quantity;

    private QuoteLine() { }

    public QuoteLine(Guid quoteId, string description, decimal unitPriceTry, int quantity)
    {
        QuoteId = quoteId;
        Description = description;
        UnitPriceTry = unitPriceTry;
        Quantity = quantity;
    }
}
