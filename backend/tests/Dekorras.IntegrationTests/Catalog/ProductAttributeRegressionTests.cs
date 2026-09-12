using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - "her ürün+özellik türü çifti için tek bir değer"
/// upsert kuralının (uygulama katmanında, migration gerektirmeden uygulanan) ve LoadCollectionAsync
/// deseninin doğru çalıştığını kanıtlar.</summary>
public sealed class ProductAttributeRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasProductAttributeTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task AyniOzellikTuruIkinciKezAyarlanincaGuncellerYeniSatirEklemez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-ozellik-urun", "OZELLIK-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null), CancellationToken.None);

        var attributeId = await new CreateProductAttributeCommandHandler(unitOfWork)
            .Handle(new CreateProductAttributeCommand("Renk"), CancellationToken.None);

        await new SetProductAttributeValueCommandHandler(unitOfWork)
            .Handle(new SetProductAttributeValueCommand(productId, attributeId, "Kırmızı"), CancellationToken.None);

        var valuesAfterFirst = await new GetProductAttributeValuesQueryHandler(unitOfWork)
            .Handle(new GetProductAttributeValuesQuery(productId), CancellationToken.None);
        Assert.Single(valuesAfterFirst);
        Assert.Equal("Kırmızı", valuesAfterFirst.Single().Value);

        // Aynı özellik türü için İKİNCİ kez değer ayarlanınca yeni bir satır EKLENMEMELİ, mevcut
        // GÜNCELLENMELİ - bu, upsert mantığının asıl regresyon senaryosu.
        await new SetProductAttributeValueCommandHandler(unitOfWork)
            .Handle(new SetProductAttributeValueCommand(productId, attributeId, "Mavi"), CancellationToken.None);

        var valuesAfterSecond = await new GetProductAttributeValuesQueryHandler(unitOfWork)
            .Handle(new GetProductAttributeValuesQuery(productId), CancellationToken.None);
        Assert.Single(valuesAfterSecond);
        Assert.Equal("Mavi", valuesAfterSecond.Single().Value);
        Assert.Equal("Renk", valuesAfterSecond.Single().AttributeName);

        // Storefront ürün sayfası da (spesifikasyon tablosu) güncel değeri yansıtmalı.
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);
        var productDetail = await new GetProductBySlugQueryHandler(unitOfWork)
            .Handle(new GetProductBySlugQuery("test-ozellik-urun", "tr"), CancellationToken.None);
        Assert.NotNull(productDetail);
        var spec = Assert.Single(productDetail!.Attributes);
        Assert.Equal("Renk", spec.Name);
        Assert.Equal("Mavi", spec.Value);
    }

    [Fact]
    public async Task OzellikDegeriSilinince_UrunSayfasindaGorunmez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-ozellik-silme-urun", "OZELLIK-002", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null), CancellationToken.None);

        var attributeId = await new CreateProductAttributeCommandHandler(unitOfWork)
            .Handle(new CreateProductAttributeCommand("Malzeme"), CancellationToken.None);

        var valueId = await new SetProductAttributeValueCommandHandler(unitOfWork)
            .Handle(new SetProductAttributeValueCommand(productId, attributeId, "Pamuk"), CancellationToken.None);

        await new RemoveProductAttributeValueCommandHandler(unitOfWork)
            .Handle(new RemoveProductAttributeValueCommand(productId, valueId), CancellationToken.None);

        var values = await new GetProductAttributeValuesQueryHandler(unitOfWork)
            .Handle(new GetProductAttributeValuesQuery(productId), CancellationToken.None);
        Assert.Empty(values);
    }

    [Fact]
    public async Task AyniIsimliOzellikTuruIkinciKezOlusturulamaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        await new CreateProductAttributeCommandHandler(unitOfWork).Handle(new CreateProductAttributeCommand("Boyut"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateProductAttributeCommandHandler(unitOfWork)
            .Handle(new CreateProductAttributeCommand("Boyut"), CancellationToken.None));
    }
}
