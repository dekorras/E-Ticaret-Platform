using Dekorras.Domain.Common;

namespace Dekorras.Domain.Accounting;

public enum InvoiceStatus
{
    Draft,          // Taslak
    Waybilled,      // İrsaliyeleşmiş
    Invoiced,       // Faturalaşmış
    EInvoiceIssued, // Faturalaşmış (E-Fatura)
    EArchiveIssued  // Faturalaşmış (E-Arşiv)
}

/// <summary>
/// Bir sipariş "Tamamlandı" durumuna geçtiğinde otomatik taslak olarak oluşturulur
/// (bkz. Ordering.OrderCompletedEvent). Resmi e-Fatura/e-Arşiv kesimi IEInvoiceProvider
/// üzerinden sertifikalı bir entegratöre devredilir - bu sınıf o işlemi kendisi yapmaz.
/// </summary>
public class Invoice : AuditableEntity
{
    public string InvoiceNumber { get; private set; } = default!;
    public Guid LedgerAccountId { get; private set; }
    public Guid? OrderId { get; private set; }
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public decimal SubTotalTry { get; private set; }
    public decimal TaxTotalTry { get; private set; }
    public decimal GrandTotalTry { get; private set; }
    public bool IsExportInvoice { get; private set; } // yurt dışı satış - KDV istisnası (%0)
    public DateTime? IssuedAtUtc { get; private set; }

    private readonly List<InvoiceLine> _lines = [];
    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    private Invoice() { }

    public Invoice(string invoiceNumber, Guid ledgerAccountId, Guid? orderId, bool isExportInvoice = false)
    {
        InvoiceNumber = invoiceNumber;
        LedgerAccountId = ledgerAccountId;
        OrderId = orderId;
        IsExportInvoice = isExportInvoice;
    }

    public void AddLine(string description, decimal unitPriceTry, int quantity, decimal taxRatePercentage)
    {
        _lines.Add(new InvoiceLine(Id, description, unitPriceTry, quantity, IsExportInvoice ? 0 : taxRatePercentage));
        Recalculate();
    }

    private void Recalculate()
    {
        SubTotalTry = _lines.Sum(l => l.LineTotalTry);
        TaxTotalTry = _lines.Sum(l => l.LineTaxTry);
        GrandTotalTry = SubTotalTry + TaxTotalTry;
    }

    public void MarkAsWaybilled() => Status = InvoiceStatus.Waybilled;

    public void IssueAsEInvoice(string officialInvoiceNumber)
    {
        InvoiceNumber = officialInvoiceNumber;
        Status = InvoiceStatus.EInvoiceIssued;
        IssuedAtUtc = DateTime.UtcNow;
    }

    public void IssueAsEArchive(string officialInvoiceNumber)
    {
        InvoiceNumber = officialInvoiceNumber;
        Status = InvoiceStatus.EArchiveIssued;
        IssuedAtUtc = DateTime.UtcNow;
    }
}

public class InvoiceLine : BaseEntity
{
    public Guid InvoiceId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal UnitPriceTry { get; private set; }
    public int Quantity { get; private set; }
    public decimal TaxRatePercentage { get; private set; }

    public decimal LineTotalTry => UnitPriceTry * Quantity;
    public decimal LineTaxTry => LineTotalTry * TaxRatePercentage / 100m;

    private InvoiceLine() { }

    public InvoiceLine(Guid invoiceId, string description, decimal unitPriceTry, int quantity, decimal taxRatePercentage)
    {
        InvoiceId = invoiceId;
        Description = description;
        UnitPriceTry = unitPriceTry;
        Quantity = quantity;
        TaxRatePercentage = taxRatePercentage;
    }
}

public class Waybill : AuditableEntity
{
    public string WaybillNumber { get; private set; } = default!;
    public Guid LedgerAccountId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public DateTime IssuedAtUtc { get; private set; } = DateTime.UtcNow;

    private Waybill() { }

    public Waybill(string waybillNumber, Guid ledgerAccountId, Guid? orderId, Guid? invoiceId)
    {
        WaybillNumber = waybillNumber;
        LedgerAccountId = ledgerAccountId;
        OrderId = orderId;
        InvoiceId = invoiceId;
    }
}

public class EInvoiceLog : BaseEntity
{
    public Guid InvoiceId { get; private set; }
    public Guid IntegrationProviderId { get; private set; } // BizimHesap/Nilvera/Uyumsoft/Foriba/İzibiz
    public bool Success { get; private set; }
    public string? GibDocumentNumber { get; private set; }
    public string? ResponseMessage { get; private set; }
    public DateTime SentAtUtc { get; private set; } = DateTime.UtcNow;

    private EInvoiceLog() { }

    public EInvoiceLog(Guid invoiceId, Guid integrationProviderId, bool success, string? gibDocumentNumber, string? responseMessage)
    {
        InvoiceId = invoiceId;
        IntegrationProviderId = integrationProviderId;
        Success = success;
        GibDocumentNumber = gibDocumentNumber;
        ResponseMessage = responseMessage;
    }
}
