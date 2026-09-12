using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Common;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - plan §2.4'ün "Bağlantılar" sekmesi: "ilgili ürünler"
/// Faz 0/1'de hiç modellenmemişti (orphaned bir entity DEĞİL, tamamen eksik bir özellikti - yeni bir
/// `RelatedProduct` join tablosu ve migration bu turda eklendi).</summary>
public sealed class RelatedProductRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasRelatedProductTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task IlgiliUrunEklenirIkinciKezEklenmezVeKaldirilabilir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var productHandler = new CreateProductCommandHandler(unitOfWork);

        var productAId = await productHandler.Handle(new CreateProductCommand(
            "test-ilgili-urun-a", "REL-A", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Ürün A", Description: null), CancellationToken.None);
        var productBId = await productHandler.Handle(new CreateProductCommand(
            "test-ilgili-urun-b", "REL-B", 150m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Ürün B", Description: null), CancellationToken.None);

        await new AddRelatedProductCommandHandler(unitOfWork).Handle(new AddRelatedProductCommand(productAId, productBId), CancellationToken.None);

        var relatedAfterFirst = await new GetRelatedProductsQueryHandler(unitOfWork).Handle(new GetRelatedProductsQuery(productAId, "tr"), CancellationToken.None);
        Assert.Single(relatedAfterFirst);
        Assert.Equal(productBId, relatedAfterFirst.Single().Id);

        // İKİNCİ kez aynı ilişkiyi eklemek yinelenen bir satır OLUŞTURMAMALI.
        await new AddRelatedProductCommandHandler(unitOfWork).Handle(new AddRelatedProductCommand(productAId, productBId), CancellationToken.None);
        var relatedAfterSecond = await new GetRelatedProductsQueryHandler(unitOfWork).Handle(new GetRelatedProductsQuery(productAId, "tr"), CancellationToken.None);
        Assert.Single(relatedAfterSecond);

        // İlişki TEK YÖNLÜDÜR - B'nin ilgili ürün listesinde A OTOMATİK olarak görünmemeli.
        var relatedForB = await new GetRelatedProductsQueryHandler(unitOfWork).Handle(new GetRelatedProductsQuery(productBId, "tr"), CancellationToken.None);
        Assert.Empty(relatedForB);

        await new RemoveRelatedProductCommandHandler(unitOfWork).Handle(new RemoveRelatedProductCommand(productAId, productBId), CancellationToken.None);
        var relatedAfterRemove = await new GetRelatedProductsQueryHandler(unitOfWork).Handle(new GetRelatedProductsQuery(productAId, "tr"), CancellationToken.None);
        Assert.Empty(relatedAfterRemove);
    }

    [Fact]
    public async Task BirUrunKendisiyleIliskilendirilemez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-ilgili-kendisi-urun", "REL-SELF", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Ürün", Description: null), CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(() => new AddRelatedProductCommandHandler(unitOfWork)
            .Handle(new AddRelatedProductCommand(productId, productId), CancellationToken.None));
    }

    [Fact]
    public async Task TaslakIlgiliUrunStorefrondaGorunmezAmaAdminGorur()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var productHandler = new CreateProductCommandHandler(unitOfWork);

        var mainProductId = await productHandler.Handle(new CreateProductCommand(
            "test-ilgili-ana-urun", "REL-MAIN", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Ana Ürün", Description: null), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(mainProductId, Published: true), CancellationToken.None);

        // İlgili ürün BİLİNÇLİ OLARAK yayınlanmadı - Taslak durumda kalır.
        var draftRelatedId = await productHandler.Handle(new CreateProductCommand(
            "test-ilgili-taslak-urun", "REL-DRAFT", 200m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Taslak İlgili Ürün", Description: null), CancellationToken.None);

        await new AddRelatedProductCommandHandler(unitOfWork).Handle(new AddRelatedProductCommand(mainProductId, draftRelatedId), CancellationToken.None);

        // Admin (durumdan bağımsız) hâlâ görmeli.
        var adminView = await new GetRelatedProductsQueryHandler(unitOfWork).Handle(new GetRelatedProductsQuery(mainProductId, "tr"), CancellationToken.None);
        Assert.Single(adminView);
        Assert.Equal(ProductStatus.Draft, adminView.Single().Status);

        // Storefront yalnızca Aktif ilgili ürünleri göstermeli - taslak SESSİZCE düşmeli.
        var storefrontView = await new GetActiveRelatedProductsQueryHandler(unitOfWork).Handle(new GetActiveRelatedProductsQuery(mainProductId, "tr"), CancellationToken.None);
        Assert.Empty(storefrontView);
    }
}
