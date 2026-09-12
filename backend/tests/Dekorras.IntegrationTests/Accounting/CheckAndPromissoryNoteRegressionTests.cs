using Dekorras.Application.Accounting.Commands;
using Dekorras.Domain.Accounting;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Accounting;

/// <summary>
/// Gerçek SQL Server'a karşı çalışır - bkz. LedgerAccountRegressionTests'teki aynı desen. Asıl
/// doğrulanan davranış: LedgerAccount.CheckNoteBalanceTry, bir çek/senet eklendiğinde artmalı,
/// tahsil/karşılıksız olarak işaretlendiğinde (artık "bekleyen" olmadığı için) azalmalıdır - bu
/// alan Faz 0/1'den beri domain'de vardı ama hiçbir yazma yolu yoktu.
/// </summary>
public sealed class CheckAndPromissoryNoteRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCheckNoteTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task Cek_EklenmesiVeTahsilEdilmesi_BekleyenBakiyeyiDogruGunceller()
    {
        Guid accountId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var account = new LedgerAccount("Test Çek Cari", LedgerAccountType.Customer);
            await unitOfWork.Repository<LedgerAccount>().AddAsync(account, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            accountId = account.Id;
        }

        Guid checkId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new CreateCheckCommandHandler(new UnitOfWork(dbContext));
            checkId = await handler.Handle(new CreateCheckCommand(accountId, "CEK-001", 1500m, DateTime.UtcNow.AddDays(30)), CancellationToken.None);
        }

        await using (var verifyContext = CreateDbContext())
        {
            var account = await verifyContext.Set<LedgerAccount>().FirstAsync(a => a.Id == accountId);
            Assert.Equal(1500m, account.CheckNoteBalanceTry); // çek eklendi - bekleyen bakiye arttı
        }

        // Tahsil edilince bekleyen bakiye düşmeli.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new MarkCheckCollectedCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new MarkCheckCollectedCommand(checkId), CancellationToken.None);
        }

        await using var finalContext = CreateDbContext();
        var finalAccount = await finalContext.Set<LedgerAccount>().FirstAsync(a => a.Id == accountId);
        Assert.Equal(0m, finalAccount.CheckNoteBalanceTry);
        var check = await finalContext.Set<Check>().FirstAsync(c => c.Id == checkId);
        Assert.Equal(PaperInstrumentStatus.Collected, check.Status);
    }

    [Fact]
    public async Task SonuclandirilmisCekTekrarTahsilEdilmeyeCalisilirsa_Reddedilir()
    {
        Guid accountId, checkId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var newAccount = new LedgerAccount("Test Çek Cari 2", LedgerAccountType.Customer);
            await unitOfWork.Repository<LedgerAccount>().AddAsync(newAccount, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            accountId = newAccount.Id;
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new CreateCheckCommandHandler(new UnitOfWork(dbContext));
            checkId = await handler.Handle(new CreateCheckCommand(accountId, "CEK-002", 800m, DateTime.UtcNow.AddDays(15)), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new MarkCheckBouncedCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new MarkCheckBouncedCommand(checkId), CancellationToken.None);
        }

        // Karşılıksız olarak işaretlenmiş bir çek TEKRAR tahsil edilmeye çalışılırsa reddedilmeli
        // (aksi halde CheckNoteBalanceTry ikinci kez düşürülüp yanlış bir negatif değere giderdi).
        await using (var dbContext = CreateDbContext())
        {
            var handler = new MarkCheckCollectedCommandHandler(new UnitOfWork(dbContext));
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new MarkCheckCollectedCommand(checkId), CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext();
        var account = await verifyContext.Set<LedgerAccount>().FirstAsync(a => a.Id == accountId);
        Assert.Equal(0m, account.CheckNoteBalanceTry); // yalnızca BİR kez düşülmüş olmalı
    }
}
