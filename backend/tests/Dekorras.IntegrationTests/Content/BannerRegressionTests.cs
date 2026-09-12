using Dekorras.Application.Content.Commands;
using Dekorras.Application.Content.Queries;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Content;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.Content.Banner` Faz 0/1'den beri vardı
/// (dosya yükleme dahil hiçbir Application/UI katmanı yoktu, hatta Activate/Deactivate/UpdateDetails
/// domain metotları bile eksikti). Aktif/pasif filtrelemenin ve sıralamanın doğru çalıştığını
/// kanıtlar - gerçek dosya yükleme (`IFileStorage`) gerektirdiği için bu kısım burada değil,
/// canlı HTTP smoke testinde doğrulanmıştır (bkz. backend/README.md).</summary>
public sealed class BannerRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasBannerTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task StorefrontYalnizcaAktifBannerlariGosterimSirasinaGoreDonderir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var banner1 = new Dekorras.Domain.Content.Banner("https://example.com/1.jpg", null, displayOrder: 1);
        var banner2 = new Dekorras.Domain.Content.Banner("https://example.com/2.jpg", "/Category?slug=kampanyalar", displayOrder: 0);
        var passiveBanner = new Dekorras.Domain.Content.Banner("https://example.com/3.jpg", null, displayOrder: 2);
        passiveBanner.Deactivate();

        dbContext.Set<Dekorras.Domain.Content.Banner>().AddRange(banner1, banner2, passiveBanner);
        await dbContext.SaveChangesAsync();

        var activeBanners = await new GetActiveBannersQueryHandler(unitOfWork).Handle(new GetActiveBannersQuery(), CancellationToken.None);
        Assert.Equal(2, activeBanners.Count);
        // DisplayOrder'a göre sıralı: banner2 (0) önce, banner1 (1) sonra - pasif banner3 hiç görünmemeli.
        Assert.Equal(banner2.Id, activeBanners.First().Id);
        Assert.Equal(banner1.Id, activeBanners.Last().Id);

        var allBanners = await new GetBannersQueryHandler(unitOfWork).Handle(new GetBannersQuery(), CancellationToken.None);
        Assert.Equal(3, allBanners.Count); // Admin listesi pasif olanı da göstermeli

        await new SetBannerActiveCommandHandler(unitOfWork).Handle(new SetBannerActiveCommand(passiveBanner.Id, true), CancellationToken.None);
        var activeBannersAfterActivation = await new GetActiveBannersQueryHandler(unitOfWork).Handle(new GetActiveBannersQuery(), CancellationToken.None);
        Assert.Equal(3, activeBannersAfterActivation.Count);

        await new RemoveBannerCommandHandler(unitOfWork, new NullFileStorage()).Handle(new RemoveBannerCommand(banner1.Id), CancellationToken.None);
        var bannersAfterRemove = await new GetBannersQueryHandler(unitOfWork).Handle(new GetBannersQuery(), CancellationToken.None);
        Assert.Equal(2, bannersAfterRemove.Count);
    }

    private sealed class NullFileStorage : Dekorras.Application.Common.Interfaces.IFileStorage
    {
        public Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");

        public Task DeleteAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
