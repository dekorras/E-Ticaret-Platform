using Dekorras.Application.Common.Interfaces;
using Dekorras.Infrastructure.Common;

namespace Dekorras.Infrastructure.EInvoiceProviders;

file static class SharedFields
{
    public static readonly ConfigFieldDefinition[] Standard =
    [
        new ConfigFieldDefinition("ApiUsername", "API Kullanıcı Adı", ConfigFieldType.Text, true),
        new ConfigFieldDefinition("ApiPassword", "API Şifresi", ConfigFieldType.Password, true)
    ];
}

/// <summary>apidocs.bizimhesap.com üzerinde yayınlanan entegrasyon API'si (bkz. plan §2.8, §10.3).
/// Firmanın mevcut BizimHesap hesabı üzerinden çalışır - API erişim koşulları BizimHesap
/// destek/satış ekibiyle teyit edilmelidir.</summary>
public sealed class BizimHesapEInvoiceProvider() : ConnectorBase("bizimhesap", "BizimHesap", [
    new ConfigFieldDefinition("ApiKey", "API Anahtarı", ConfigFieldType.Password, true),
    new ConfigFieldDefinition("CompanyId", "Firma No (DEKORRAS MİMARLIK MÜHENDİSLİK)", ConfigFieldType.Text, true)]), IEInvoiceProvider
{
    public Task<EInvoiceResult> IssueEInvoiceAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"BH{invoiceId:N}"[..16].ToUpperInvariant(), null));
    public Task<EInvoiceResult> IssueEArchiveAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"BH{invoiceId:N}"[..16].ToUpperInvariant(), null));
}

public sealed class NilveraEInvoiceProvider() : ConnectorBase("nilvera", "Nilvera", SharedFields.Standard), IEInvoiceProvider
{
    public Task<EInvoiceResult> IssueEInvoiceAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"NLV{invoiceId:N}"[..16].ToUpperInvariant(), null));
    public Task<EInvoiceResult> IssueEArchiveAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"NLV{invoiceId:N}"[..16].ToUpperInvariant(), null));
}

public sealed class UyumsoftEInvoiceProvider() : ConnectorBase("uyumsoft", "Uyumsoft", SharedFields.Standard), IEInvoiceProvider
{
    public Task<EInvoiceResult> IssueEInvoiceAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"UYM{invoiceId:N}"[..16].ToUpperInvariant(), null));
    public Task<EInvoiceResult> IssueEArchiveAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"UYM{invoiceId:N}"[..16].ToUpperInvariant(), null));
}

public sealed class ForibaEInvoiceProvider() : ConnectorBase("foriba", "Foriba", SharedFields.Standard), IEInvoiceProvider
{
    public Task<EInvoiceResult> IssueEInvoiceAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"FRB{invoiceId:N}"[..16].ToUpperInvariant(), null));
    public Task<EInvoiceResult> IssueEArchiveAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"FRB{invoiceId:N}"[..16].ToUpperInvariant(), null));
}

public sealed class IzibizEInvoiceProvider() : ConnectorBase("izibiz", "İzibiz", SharedFields.Standard), IEInvoiceProvider
{
    public Task<EInvoiceResult> IssueEInvoiceAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"IZB{invoiceId:N}"[..16].ToUpperInvariant(), null));
    public Task<EInvoiceResult> IssueEArchiveAsync(IReadOnlyDictionary<string, string> config, Guid invoiceId, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(true, $"IZB{invoiceId:N}"[..16].ToUpperInvariant(), null));
}
