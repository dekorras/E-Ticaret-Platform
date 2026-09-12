using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - ürünün GTİP/HS kodu ve paket boyutlarının (Weight'e
/// KARIŞMADAN) doğru kaydedildiğini kanıtlar - bu alanlar Faz 0/1'den beri domain'de vardı
/// (`Product.SetHsCode`/`SetPackageDimensions`) ama hiç Application katmanı yoktu.</summary>
public sealed class ProductPhysicalInfoRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasProductPhysicalInfoTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task HsKoduVeBoyutlarKaydedilirVeAgirligaKarismaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-fiziksel-urun", "FIZIKSEL-001", 200m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null,
            WeightKg: 2m, HsCode: "6304.92", LengthCm: 30m, WidthCm: 20m, HeightCm: 10m), CancellationToken.None);

        var product = await dbContext.Set<Product>().FirstAsync(p => p.Id == productId);
        Assert.Equal("6304.92", product.HsCode);
        Assert.Equal(30m, product.Length);
        Assert.Equal(20m, product.Width);
        Assert.Equal(10m, product.Height);
        Assert.Equal("cm", product.DimensionUnit);
        Assert.Equal(2m, product.Weight); // boyutlar ağırlığı EZMEMELİ
        Assert.Equal("kg", product.WeightUnit);

        // Admin ekranının kullandığı sorgu da doğru döndürmeli.
        var detail = await new GetProductByIdQueryHandler(unitOfWork).Handle(new GetProductByIdQuery(productId, "tr"), CancellationToken.None);
        Assert.Equal("6304.92", detail!.HsCode);
        Assert.Equal(30m, detail.LengthCm);
        Assert.Equal(20m, detail.WidthCm);
        Assert.Equal(10m, detail.HeightCm);
        Assert.Equal(2m, detail.WeightKg);

        // Yalnızca BOYUTLARI güncellemek (UpdateProductCommand ile) ağırlığa DOKUNMAMALI - ikisi
        // bağımsız iki alan grubu olarak ayrı ayrı düzenlenebilmeli (bkz. SetPackageDimensions belgesi).
        await new UpdateProductCommandHandler(unitOfWork).Handle(new UpdateProductCommand(
            productId, "test-fiziksel-urun", "FIZIKSEL-001", 200m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null,
            WeightKg: 2m, HsCode: "6304.92", LengthCm: 40m, WidthCm: 25m, HeightCm: 15m), CancellationToken.None);

        var productAfterUpdate = await dbContext.Set<Product>().FirstAsync(p => p.Id == productId);
        Assert.Equal(40m, productAfterUpdate.Length);
        Assert.Equal(2m, productAfterUpdate.Weight); // hâlâ değişmemiş
    }

    [Fact]
    public async Task TanimlayicilarVeMetaRobotsKaydedilir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-tanimlayici-urun", "TANIMLAYICI-001", 150m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null,
            Sku: "SKU-1", Upc: "UPC-1", Ean: "EAN-1", Jan: "JAN-1", Isbn: "ISBN-1", Mpn: "MPN-1",
            MetaRobots: "noindex,nofollow"), CancellationToken.None);

        var product = await dbContext.Set<Product>().FirstAsync(p => p.Id == productId);
        Assert.Equal("SKU-1", product.Sku);
        Assert.Equal("UPC-1", product.Upc);
        Assert.Equal("EAN-1", product.Ean);
        Assert.Equal("JAN-1", product.Jan);
        Assert.Equal("ISBN-1", product.Isbn);
        Assert.Equal("MPN-1", product.Mpn);
        Assert.Equal("noindex,nofollow", product.MetaRobots);

        var detail = await new GetProductByIdQueryHandler(unitOfWork).Handle(new GetProductByIdQuery(productId, "tr"), CancellationToken.None);
        Assert.Equal("SKU-1", detail!.Sku);
        Assert.Equal("noindex,nofollow", detail.MetaRobots);

        // Storefront'un MetaRobots'u <meta name="robots"> olarak render edebilmesi için kullandığı
        // sorgu da (GetProductBySlugQuery) doğru döndürmeli.
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);
        var storefrontDetail = await new Dekorras.Application.Catalog.Storefront.GetProductBySlugQueryHandler(unitOfWork)
            .Handle(new Dekorras.Application.Catalog.Storefront.GetProductBySlugQuery("test-tanimlayici-urun", "tr"), CancellationToken.None);
        Assert.Equal("noindex,nofollow", storefrontDetail!.MetaRobots);
    }
}
