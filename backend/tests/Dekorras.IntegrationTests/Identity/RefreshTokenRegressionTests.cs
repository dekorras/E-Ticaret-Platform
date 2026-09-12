using Dekorras.Application.Identity.Commands;
using Dekorras.Domain.Identity;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Identity;

/// <summary>
/// Gerçek SQL Server'a karşı çalışır. Her adım kendi taze DbContext'ini kullanır - bkz.
/// CategoryUpdateRegressionTests'teki açıklama. Buradaki en kritik davranış: bir yenileme jetonu
/// rotasyondan sonra TEKRAR kullanılmaya çalışılırsa (çalınmış bir jetonun tekrar oynatılması
/// senaryosu) reddedilmelidir.
/// </summary>
public sealed class RefreshTokenRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasRefreshTokenTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task Rotasyon_EskiJetonuGecersizKilarVeYeniJetonuDogruKullaniciyaVerir()
    {
        const string identityUserId = "user-abc";

        await using (var dbContext = CreateDbContext())
        {
            var handler = new IssueRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new IssueRefreshTokenCommand(identityUserId, "hash-v1", DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        }

        string? returnedUserId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RotateRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            returnedUserId = await handler.Handle(new RotateRefreshTokenCommand("hash-v1", "hash-v2", DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        }

        Assert.Equal(identityUserId, returnedUserId);

        await using var verifyContext = CreateDbContext();
        var tokens = await verifyContext.Set<RefreshToken>().Where(t => t.IdentityUserId == identityUserId).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.False(tokens.Single(t => t.TokenHash == "hash-v1").IsActive); // eski jeton iptal edildi
        Assert.True(tokens.Single(t => t.TokenHash == "hash-v2").IsActive); // yeni jeton aktif
    }

    [Fact]
    public async Task RotasyondanSonra_EskiJetonTekrarKullanilmayaCalisilirsa_Reddedilir()
    {
        await using (var dbContext = CreateDbContext())
        {
            var handler = new IssueRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new IssueRefreshTokenCommand("user-xyz", "hash-a", DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        }

        // İlk rotasyon - meşru istemci
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RotateRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            var result = await handler.Handle(new RotateRefreshTokenCommand("hash-a", "hash-b", DateTime.UtcNow.AddDays(14)), CancellationToken.None);
            Assert.NotNull(result);
        }

        // "hash-a" ÇALINMIŞ bir jeton gibi TEKRAR kullanılmaya çalışılıyor - reddedilmeli.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RotateRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            var replayResult = await handler.Handle(new RotateRefreshTokenCommand("hash-a", "hash-c", DateTime.UtcNow.AddDays(14)), CancellationToken.None);
            Assert.Null(replayResult);
        }

        // Var olmayan/süresi dolmuş bir jetonla rotasyon da reddedilmeli.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RotateRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            var unknownResult = await handler.Handle(new RotateRefreshTokenCommand("hic-var-olmayan-hash", "hash-d", DateTime.UtcNow.AddDays(14)), CancellationToken.None);
            Assert.Null(unknownResult);
        }
    }

    [Fact]
    public async Task Cikis_AktifJetonuIptalEder()
    {
        await using (var dbContext = CreateDbContext())
        {
            var handler = new IssueRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new IssueRefreshTokenCommand("user-logout", "hash-logout", DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new RevokeRefreshTokenCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new RevokeRefreshTokenCommand("hash-logout"), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var token = await verifyContext.Set<RefreshToken>().FirstAsync(t => t.TokenHash == "hash-logout");
        Assert.False(token.IsActive);
    }
}
