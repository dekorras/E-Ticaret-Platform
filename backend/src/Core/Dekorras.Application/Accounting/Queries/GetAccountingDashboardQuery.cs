using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record AccountingDashboardDto(
    decimal TotalCashBalanceTry,
    decimal TotalBankBalanceTry,
    decimal TotalReceivableTry, // cari hesaplardan bize borçlu olunan toplam (pozitif açık bakiyeler)
    decimal TotalPayableTry, // cari hesaplara bizim borçlu olduğumuz toplam (negatif açık bakiyeler, mutlak değer)
    decimal TotalPendingCheckNoteTry,
    decimal TotalAssetsTry, // Varlıklar = Kasa + Banka + Alacaklar
    decimal TotalLiabilitiesTry, // Borçlar = Borçlar + bekleyen çek/senet
    decimal TotalExpensesThisMonthTry,
    decimal TotalExpensesAllTimeTry,
    decimal TotalDraftInvoicesTry,
    int ActiveLedgerAccountCount,
    int PendingCheckCount,
    int PendingNoteCount);

public sealed record GetAccountingDashboardQuery : IRequest<AccountingDashboardDto>;

public sealed class GetAccountingDashboardQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetAccountingDashboardQuery, AccountingDashboardDto>
{
    public Task<AccountingDashboardDto> Handle(GetAccountingDashboardQuery request, CancellationToken cancellationToken)
    {
        var totalCash = unitOfWork.Repository<CashRegister>().Query().Sum(r => (decimal?)r.BalanceTry) ?? 0m;
        var totalBank = unitOfWork.Repository<BankAccount>().Query().Sum(a => (decimal?)a.BalanceTry) ?? 0m;

        var ledgerAccounts = unitOfWork.Repository<LedgerAccount>().Query()
            .Select(a => new { a.OpenBalanceTry, a.CheckNoteBalanceTry, a.IsActive })
            .ToList();

        var totalReceivable = ledgerAccounts.Where(a => a.OpenBalanceTry > 0).Sum(a => a.OpenBalanceTry);
        var totalPayable = ledgerAccounts.Where(a => a.OpenBalanceTry < 0).Sum(a => -a.OpenBalanceTry);
        var totalPendingCheckNote = ledgerAccounts.Sum(a => a.CheckNoteBalanceTry);
        var activeLedgerAccountCount = ledgerAccounts.Count(a => a.IsActive);

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var expenses = unitOfWork.Repository<Expense>().Query().Select(e => new { e.AmountTry, e.ExpenseDateUtc }).ToList();
        var totalExpensesThisMonth = expenses.Where(e => e.ExpenseDateUtc >= monthStart).Sum(e => e.AmountTry);
        var totalExpensesAllTime = expenses.Sum(e => e.AmountTry);

        var totalDraftInvoices = unitOfWork.Repository<Invoice>().Query()
            .Where(i => i.Status == InvoiceStatus.Draft)
            .Sum(i => (decimal?)i.GrandTotalTry) ?? 0m;

        var pendingCheckCount = unitOfWork.Repository<Check>().Query().Count(c => c.Status == PaperInstrumentStatus.Pending);
        var pendingNoteCount = unitOfWork.Repository<PromissoryNote>().Query().Count(n => n.Status == PaperInstrumentStatus.Pending);

        var dto = new AccountingDashboardDto(
            totalCash, totalBank, totalReceivable, totalPayable, totalPendingCheckNote,
            TotalAssetsTry: totalCash + totalBank + totalReceivable,
            TotalLiabilitiesTry: totalPayable + totalPendingCheckNote,
            totalExpensesThisMonth, totalExpensesAllTime, totalDraftInvoices,
            activeLedgerAccountCount, pendingCheckCount, pendingNoteCount);

        return Task.FromResult(dto);
    }
}
