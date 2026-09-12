using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - ürün videosu ekleme/silmenin (LoadCollectionAsync
/// deseni) ve Storefront yansımasının doğru çalıştığını kanıtlar.</summary>
public sealed class ProductVideoRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasProductVideoTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task IkinciVideoEklemekVeSilmekHataVermez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-video-urun", "VIDEO-001", 200m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null), CancellationToken.None);

        var firstVideoId = await new AddProductVideoCommandHandler(unitOfWork)
            .Handle(new AddProductVideoCommand(productId, "https://youtube.com/watch?v=1", "Tanıtım Videosu"), CancellationToken.None);

        // İKİNCİ video ekleme - client-taraflı Guid + already-tracked aggregate senaryosunda EF
        // Core'un "Modified sanıp UPDATE dener" hatasını (bkz. backend/README.md "Önemli mimari not")
        // tekrar etmediğini kanıtlayan asıl regresyon senaryosu.
        var secondVideoId = await new AddProductVideoCommandHandler(unitOfWork)
            .Handle(new AddProductVideoCommand(productId, "https://youtube.com/watch?v=2", null), CancellationToken.None);

        var videos = await new GetProductVideosQueryHandler(unitOfWork).Handle(new GetProductVideosQuery(productId), CancellationToken.None);
        Assert.Equal(2, videos.Count);
        Assert.Contains(videos, v => v.Id == firstVideoId && v.Title == "Tanıtım Videosu");
        Assert.Contains(videos, v => v.Id == secondVideoId && v.Title == null);

        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);
        var productDetail = await new GetProductBySlugQueryHandler(unitOfWork)
            .Handle(new GetProductBySlugQuery("test-video-urun", "tr"), CancellationToken.None);
        Assert.NotNull(productDetail);
        Assert.Equal(2, productDetail!.Videos.Count);

        await new RemoveProductVideoCommandHandler(unitOfWork).Handle(new RemoveProductVideoCommand(productId, firstVideoId), CancellationToken.None);

        var videosAfterRemove = await new GetProductVideosQueryHandler(unitOfWork).Handle(new GetProductVideosQuery(productId), CancellationToken.None);
        Assert.Single(videosAfterRemove);
        Assert.Equal(secondVideoId, videosAfterRemove.Single().Id);
    }
}
