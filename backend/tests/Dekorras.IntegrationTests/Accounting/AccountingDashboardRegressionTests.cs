using Dekorras.Application.Accounting.Commands;
using Dekorras.Application.Accounting.Queries;
using Dekorras.Domain.Accounting;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Accounting;

/// <summary>Gerçek SQL Server'a karşı çalışır - bkz. LedgerAccountRegressionTests'teki aynı desen.</summary>
public sealed class AccountingDashboardRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasDashboardTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Dashboard_VarliklarVeBorclariDogruToplar()
    {
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);

            var cashRegister = new CashRegister("Test Kasa");
            cashRegister.Deposit(1000m);
            await unitOfWork.Repository<CashRegister>().AddAsync(cashRegister, CancellationToken.None);

            var bankAccount = new BankAccount("Test Bank", "TR000000000000000000000000");
            bankAccount.Deposit(2000m);
            await unitOfWork.Repository<BankAccount>().AddAsync(bankAccount, CancellationToken.None);

            // Alacak: bize borçlu bir müşteri (pozitif açık bakiye).
            var receivableAccount = new LedgerAccount("Test Alacaklı Müşteri", LedgerAccountType.Customer);
            receivableAccount.RecordTransaction(LedgerTransactionDirection.Debit, 500m, "Test satış");
            await unitOfWork.Repository<LedgerAccount>().AddAsync(receivableAccount, CancellationToken.None);

            // Borç: bizim borçlu olduğumuz bir tedarikçi (negatif açık bakiye).
            var payableAccount = new LedgerAccount("Test Borçlu Tedarikçi", LedgerAccountType.Supplier);
            payableAccount.RecordTransaction(LedgerTransactionDirection.Credit, 300m, "Test alım");
            await unitOfWork.Repository<LedgerAccount>().AddAsync(payableAccount, CancellationToken.None);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            // Bekleyen çek/senet: alacaklı hesaba bir çek ekleniyor.
            var checkHandler = new CreateCheckCommandHandler(unitOfWork);
            await checkHandler.Handle(new CreateCheckCommand(receivableAccount.Id, "CEK-DASH-001", 150m, DateTime.UtcNow.AddDays(10)), CancellationToken.None);

            var expenseHandler = new CreateExpenseCommandHandler(unitOfWork);
            await expenseHandler.Handle(new CreateExpenseCommand("Kira", 400m, DateTime.UtcNow, "Test gider"), CancellationToken.None);
        }

        await using var queryContext = CreateDbContext();
        var handler = new GetAccountingDashboardQueryHandler(new UnitOfWork(queryContext));
        var dashboard = await handler.Handle(new GetAccountingDashboardQuery(), CancellationToken.None);

        Assert.Equal(1000m, dashboard.TotalCashBalanceTry);
        Assert.Equal(2000m, dashboard.TotalBankBalanceTry);
        Assert.Equal(500m, dashboard.TotalReceivableTry);
        Assert.Equal(300m, dashboard.TotalPayableTry);
        Assert.Equal(150m, dashboard.TotalPendingCheckNoteTry);
        Assert.Equal(3500m, dashboard.TotalAssetsTry); // 1000 + 2000 + 500
        Assert.Equal(450m, dashboard.TotalLiabilitiesTry); // 300 + 150
        Assert.Equal(400m, dashboard.TotalExpensesAllTimeTry);
        Assert.Equal(1, dashboard.PendingCheckCount);
    }
}
