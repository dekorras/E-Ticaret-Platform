using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - arama metni ve fiyat aralığı filtrelerinin doğru
/// SQL'e çevrildiğini (LINQ-to-Entities çevirisi hatasız çalıştığını) kanıtlar - bu sınıf hata NSubstitute
/// mock'larıyla asla yakalanamaz çünkü sahte repository LINQ ifadesini gerçekten veritabanına
/// çevirmez.</summary>
public sealed class StorefrontSearchRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasSearchTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task AramaMetniVeFiyatAraligi_DogruUrunleriDonderir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var haliHandler = new CreateProductCommandHandler(unitOfWork);
        var haliId = await haliHandler.Handle(new CreateProductCommand(
            "test-arama-hali", "ARAMA-001", 500m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Kırmızı Halı", Description: "Şık bir salon halısı"), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(haliId, Published: true), CancellationToken.None);

        var perdeId = await haliHandler.Handle(new CreateProductCommand(
            "test-arama-perde", "ARAMA-002", 150m, 20m, UnitOfMeasure.LinearMeter, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Beyaz Perde", Description: "Modern tül perde"), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(perdeId, Published: true), CancellationToken.None);

        // "Halı" araması yalnızca ilk ürünü bulmalı.
        var queryHandler = new GetStorefrontProductsQueryHandler(unitOfWork);
        var searchResult = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SearchText: "Halı"), CancellationToken.None);
        Assert.Single(searchResult.Items);
        Assert.Equal(haliId, searchResult.Items.Single().Id);
        Assert.Equal(1, searchResult.TotalCount);

        // Fiyat aralığı: 100-200 TRY yalnızca perdeyi bulmalı.
        var priceResult = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, MinPriceTry: 100m, MaxPriceTry: 200m), CancellationToken.None);
        Assert.Single(priceResult.Items);
        Assert.Equal(perdeId, priceResult.Items.Single().Id);

        // Açıklama metninde de arama yapılabilmeli ("tül" yalnızca perdenin açıklamasında geçiyor).
        var descriptionResult = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SearchText: "tül"), CancellationToken.None);
        Assert.Single(descriptionResult.Items);
        Assert.Equal(perdeId, descriptionResult.Items.Single().Id);

        // Eşleşmeyen bir arama boş liste döndürmeli.
        var noMatchResult = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SearchText: "hiçbiryerde-eşleşmeyen-xyz"), CancellationToken.None);
        Assert.Empty(noMatchResult.Items);
        Assert.Equal(0, noMatchResult.TotalCount);
    }
}
