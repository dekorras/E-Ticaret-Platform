using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - ürün karşılaştırma (bkz. plan §2.1 "ürün listeleme
/// sayfası: ... ürün karşılaştırma listesi") Faz 0/1'den beri planda vardı ama hiç Domain/Application/
/// UI katmanı yazılmamıştı (orphaned bir entity DEĞİL, tamamen eksik bir özellikti - `ProductAttribute`/
/// `ProductAttributeValue` (devamı 29) üzerine inşa edildi, DB'ye yeni bir tablo eklenmedi çünkü
/// karşılaştırma listesinin kendisi Storefront'ta bir çerezde tutulur, `StorefrontCurrency`/
/// `StorefrontLanguage` ile aynı desen).</summary>
public sealed class ProductComparisonRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasComparisonTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task FarkliOzelliklereSahipIkiUrunBirliktKarsilastirilabilir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var colorAttributeId = await new CreateProductAttributeCommandHandler(unitOfWork)
            .Handle(new CreateProductAttributeCommand("Renk"), CancellationToken.None);
        var materialAttributeId = await new CreateProductAttributeCommandHandler(unitOfWork)
            .Handle(new CreateProductAttributeCommand("Malzeme"), CancellationToken.None);

        var productAId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-karsilastirma-urun-a", "COMPARE-A", 150m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Karşılaştırma Ürünü A", Description: null), CancellationToken.None);
        await new SetProductAttributeValueCommandHandler(unitOfWork)
            .Handle(new SetProductAttributeValueCommand(productAId, colorAttributeId, "Kırmızı"), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productAId, Published: true), CancellationToken.None);

        // Ürün B'de YALNIZCA Malzeme özelliği var - Renk hiç girilmedi (bilinçli, birleşik satır
        // listesinin farklı özellik setlerini doğru işlediğini kanıtlamak için).
        var productBId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-karsilastirma-urun-b", "COMPARE-B", 200m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 3,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Karşılaştırma Ürünü B", Description: null), CancellationToken.None);
        await new SetProductAttributeValueCommandHandler(unitOfWork)
            .Handle(new SetProductAttributeValueCommand(productBId, materialAttributeId, "Ahşap"), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productBId, Published: true), CancellationToken.None);

        var result = await new GetProductsForComparisonQueryHandler(unitOfWork)
            .Handle(new GetProductsForComparisonQuery([productAId, productBId], "tr"), CancellationToken.None);

        Assert.Equal(2, result.Count);
        var productA = result.Single(p => p.Id == productAId);
        var productB = result.Single(p => p.Id == productBId);

        Assert.Equal("Karşılaştırma Ürünü A", productA.Name);
        Assert.Equal("Kırmızı", productA.Attributes["Renk"]);
        Assert.False(productA.Attributes.ContainsKey("Malzeme"));

        Assert.Equal("Ahşap", productB.Attributes["Malzeme"]);
        Assert.False(productB.Attributes.ContainsKey("Renk"));
    }

    [Fact]
    public async Task TaslakUrunKarsilastirmaListesindenSessizceDuser()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-karsilastirma-taslak-urun", "COMPARE-DRAFT", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Taslak Ürün", Description: null), CancellationToken.None);
        // BİLİNÇLİ OLARAK yayınlanmadı - varsayılan Taslak durumda kalır.

        var result = await new GetProductsForComparisonQueryHandler(unitOfWork)
            .Handle(new GetProductsForComparisonQuery([productId], "tr"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task BosUrunListesiBosSonucDoner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var result = await new GetProductsForComparisonQueryHandler(unitOfWork)
            .Handle(new GetProductsForComparisonQuery([], "tr"), CancellationToken.None);

        Assert.Empty(result);
    }
}
