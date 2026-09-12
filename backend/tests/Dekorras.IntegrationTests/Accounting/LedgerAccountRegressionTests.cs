using Dekorras.Application.Accounting.Commands;
using Dekorras.Application.Accounting.Queries;
using Dekorras.Domain.Accounting;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Accounting;

/// <summary>
/// Gerçek SQL Server'a karşı çalışır. Her adım kendi taze DbContext'ini kullanır (bkz.
/// CategoryUpdateRegressionTests'teki aynı not) - ikinci bir hareket kaydının var olan
/// Transactions koleksiyonunu doğru yüklediğini (LoadCollectionAsync) kanıtlar.
/// </summary>
public sealed class LedgerAccountRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasLedgerAccountTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task IkinciHareketEklemek_AcikBakiyeyiDogruGunceller_VeHataVermez()
    {
        Guid accountId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new CreateLedgerAccountCommandHandler(new UnitOfWork(dbContext));
            accountId = await handler.Handle(new CreateLedgerAccountCommand("Test Cari", LedgerAccountType.Customer, null, null, null), CancellationToken.None);
        }

        // İlk hareket: Borç 1000 TRY.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RecordLedgerTransactionCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new RecordLedgerTransactionCommand(accountId, LedgerTransactionDirection.Debit, 1000m, "İlk sipariş"), CancellationToken.None);
        }

        // İkinci hareket: hesap ARTIK bir harekete sahip - Transactions koleksiyonunun doğru
        // yüklenip yeni hareketin "Modified" değil "Added" olarak işaretlendiğini doğrulayan
        // asıl regresyon senaryosu. Alacak 300 TRY (kısmi ödeme).
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RecordLedgerTransactionCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new RecordLedgerTransactionCommand(accountId, LedgerTransactionDirection.Credit, 300m, "Kısmi ödeme"), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var account = await verifyContext.Set<LedgerAccount>().FirstAsync(a => a.Id == accountId);
        Assert.Equal(700m, account.OpenBalanceTry); // 1000 - 300

        var queryHandler = new GetLedgerAccountDetailQueryHandler(new UnitOfWork(verifyContext));
        var detail = await queryHandler.Handle(new GetLedgerAccountDetailQuery(accountId), CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Transactions.Count);
    }

    [Fact]
    public async Task PasifeAlmaVeTekrarAktiflestirme_DurumuDogruGunceller()
    {
        Guid accountId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new CreateLedgerAccountCommandHandler(new UnitOfWork(dbContext));
            accountId = await handler.Handle(new CreateLedgerAccountCommand("Test Aktif Pasif Cari", LedgerAccountType.Supplier, null, null, null), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new SetLedgerAccountActiveCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new SetLedgerAccountActiveCommand(accountId, IsActive: false), CancellationToken.None);
        }

        await using (var verifyContext = CreateDbContext())
        {
            var account = await verifyContext.Set<LedgerAccount>().FirstAsync(a => a.Id == accountId);
            Assert.False(account.IsActive);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new SetLedgerAccountActiveCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new SetLedgerAccountActiveCommand(accountId, IsActive: true), CancellationToken.None);
        }

        await using var finalContext = CreateDbContext();
        var finalAccount = await finalContext.Set<LedgerAccount>().FirstAsync(a => a.Id == accountId);
        Assert.True(finalAccount.IsActive);
    }
}
