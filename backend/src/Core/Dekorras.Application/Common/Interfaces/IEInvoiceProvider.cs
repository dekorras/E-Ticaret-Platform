namespace Dekorras.Application.Common.Interfaces;

public sealed record EInvoiceResult(bool Success, string? GibDocumentNumber, string? FailureReason);

/// <summary>Sertifikalı e-Fatura/e-Arşiv entegratörü sözleşmesi: BizimHesap, Nilvera, Uyumsoft, Foriba, İzibiz.
/// GİB'e resmi belge iletimi burada YAPILMAZ, sertifikalı üçüncü tarafa devredilir (bkz. plan §10.3).</summary>
public interface IEInvoiceProvider : IIntegrationConnector
{
    Task<EInvoiceResult> IssueEInvoiceAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken cancellationToken);
    Task<EInvoiceResult> IssueEArchiveAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken cancellationToken);
}
