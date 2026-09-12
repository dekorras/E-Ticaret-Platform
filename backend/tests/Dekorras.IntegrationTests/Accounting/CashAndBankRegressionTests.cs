using Dekorras.Application.Accounting.Commands;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Common;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Accounting;

/// <summary>Gerçek SQL Server'a karşı çalışır - bkz. LedgerAccountRegressionTests'teki aynı desen.</summary>
public sealed class CashAndBankRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCashBankTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task Kasa_YetersizBakiyedeCekmeyeCalisilirsa_ReddedilirVeBakiyeDegismez()
    {
        Guid registerId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new CreateCashRegisterCommandHandler(new UnitOfWork(dbContext));
            registerId = await handler.Handle(new CreateCashRegisterCommand("Test Kasa"), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new DepositToCashRegisterCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new DepositToCashRegisterCommand(registerId, 500m), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new WithdrawFromCashRegisterCommandHandler(new UnitOfWork(dbContext));
            await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new WithdrawFromCashRegisterCommand(registerId, 1000m), CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext();
        var register = await verifyContext.Set<CashRegister>().FirstAsync(r => r.Id == registerId);
        Assert.Equal(500m, register.BalanceTry); // reddedilen çekim bakiyeyi DEĞİŞTİRMEMELİ
    }

    [Fact]
    public async Task BankaHesabi_YatirVeCekIslemleriBakiyeyiDogruGunceller()
    {
        Guid accountId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new CreateBankAccountCommandHandler(new UnitOfWork(dbContext));
            accountId = await handler.Handle(new CreateBankAccountCommand("Test Bank", "TR000000000000000000000000"), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new DepositToBankAccountCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new DepositToBankAccountCommand(accountId, 1000m), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new WithdrawFromBankAccountCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new WithdrawFromBankAccountCommand(accountId, 300m), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var account = await verifyContext.Set<BankAccount>().FirstAsync(a => a.Id == accountId);
        Assert.Equal(700m, account.BalanceTry); // 1000 - 300
    }
}
