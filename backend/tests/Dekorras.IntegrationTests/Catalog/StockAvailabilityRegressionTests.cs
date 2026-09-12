using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - plan §2.4'ün "Stoktan Düş (E/H)" ve "Stok Dışı
/// Durumu (2-3 gün içinde / Ön Sipariş / Stokta var / Stokta yok)" alanları Faz 0/1'den beri
/// `Product.TrackStock`/`StockAvailability` olarak modellenmişti ama HİÇBİRİ hiçbir yerden
/// değiştirilemiyordu - `UpdateStock` yalnızca InStock/OutOfStock'u otomatik türetiyordu.</summary>
public sealed class StockAvailabilityRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasStockAvailabilityTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task StokTakibiKapatilincaOnSiparisDurumuKaliciKalir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-stok-onsiparis-urun", "STOCK-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 0,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);

        // Stok takibini KAPAT (sipariş üzerine tedarik edilen bir ürün senaryosu) ve "Ön Sipariş"
        // olarak işaretle.
        await new SetProductTrackStockCommandHandler(unitOfWork).Handle(new SetProductTrackStockCommand(productId, false), CancellationToken.None);
        await new SetProductStockAvailabilityCommandHandler(unitOfWork).Handle(new SetProductStockAvailabilityCommand(productId, StockAvailability.PreOrder), CancellationToken.None);

        var detailBefore = await new GetProductByIdQueryHandler(unitOfWork).Handle(new GetProductByIdQuery(productId, "tr"), CancellationToken.None);
        Assert.False(detailBefore!.TrackStock);
        Assert.Equal(StockAvailability.PreOrder, detailBefore.StockAvailability);

        // Stok miktarı değişse bile (ör. tedarikçiden bir parti geldi, elle güncellendi) - stok
        // takibi KAPALI olduğu için "Ön Sipariş" durumu OTOMATİK olarak ezilMEMELİ.
        var productRepository = unitOfWork.Repository<Product>();
        var product = await productRepository.GetByIdAsync(productId, CancellationToken.None);
        product!.UpdateStock(5);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var detailAfter = await new GetProductByIdQueryHandler(unitOfWork).Handle(new GetProductByIdQuery(productId, "tr"), CancellationToken.None);
        Assert.Equal(StockAvailability.PreOrder, detailAfter!.StockAvailability);

        // Storefront ürün sayfası da bu durumu doğru yansıtmalı.
        var storefrontDetail = await new GetProductBySlugQueryHandler(unitOfWork)
            .Handle(new GetProductBySlugQuery("test-stok-onsiparis-urun", "tr"), CancellationToken.None);
        Assert.Equal(StockAvailability.PreOrder, storefrontDetail!.StockAvailability);
    }

    [Fact]
    public async Task StokTakibiAcikkenStokMiktariOtomatikOlarakDurumuEzer()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-stok-otomatik-urun", "STOCK-002", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün 2", Description: null), CancellationToken.None);

        // Stok takibi VARSAYILAN olarak AÇIK - admin elle "Ön Sipariş" seçse bile bir sonraki
        // UpdateStock çağrısı bunu otomatik olarak Stokta Var/Yok'a GERİ DÖNDÜRMELİ.
        await new SetProductStockAvailabilityCommandHandler(unitOfWork).Handle(new SetProductStockAvailabilityCommand(productId, StockAvailability.PreOrder), CancellationToken.None);

        var productRepository = unitOfWork.Repository<Product>();
        var product = await productRepository.GetByIdAsync(productId, CancellationToken.None);
        product!.UpdateStock(0);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetProductByIdQueryHandler(unitOfWork).Handle(new GetProductByIdQuery(productId, "tr"), CancellationToken.None);
        Assert.True(detail!.TrackStock);
        Assert.Equal(StockAvailability.OutOfStock, detail.StockAvailability);
    }
}
