using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - çok dilli içerik modelinin (bkz. plan §7 - TR/EN/DE/
/// FR/NL/ES/AR) `Product`/`Category` için doğru çalıştığını kanıtlar: aynı ürüne/kategoriye birden
/// çok dilde çeviri eklenebiliyor, her dil kendi sorgusunda doğru döndürülüyor, biri diğerini
/// EZMİYOR (upsert per-language, bkz. SetTranslation).</summary>
public sealed class MultiLanguageRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasMultiLanguageTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task UrunuIkinciBirDildeGuncellemekIlkDildekiCeviriyiSilmezVeHerIkisiDeDogruDoner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-cokdil-urun", "COKDIL-001", 300m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Kırmızı Halı", Description: "Şık bir halı"), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);

        // Aynı ürüne İngilizce ve Arapça çeviriler EKLENİYOR - UpdateProductCommand her çağrıda
        // yalnızca kendi LanguageCode'unu upsert eder, önceki dillerin çevirisine dokunmaz.
        await new UpdateProductCommandHandler(unitOfWork).Handle(new UpdateProductCommand(
            productId, "test-cokdil-urun", "COKDIL-001", 300m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "en", Name: "Red Carpet", Description: "A stylish carpet"), CancellationToken.None);
        await new UpdateProductCommandHandler(unitOfWork).Handle(new UpdateProductCommand(
            productId, "test-cokdil-urun", "COKDIL-001", 300m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "ar", Name: "سجادة حمراء", Description: "سجادة أنيقة"), CancellationToken.None);

        var trDetail = await new GetProductBySlugQueryHandler(unitOfWork).Handle(new GetProductBySlugQuery("test-cokdil-urun", "tr"), CancellationToken.None);
        var enDetail = await new GetProductBySlugQueryHandler(unitOfWork).Handle(new GetProductBySlugQuery("test-cokdil-urun", "en"), CancellationToken.None);
        var arDetail = await new GetProductBySlugQueryHandler(unitOfWork).Handle(new GetProductBySlugQuery("test-cokdil-urun", "ar"), CancellationToken.None);

        Assert.Equal("Kırmızı Halı", trDetail!.Name);
        Assert.Equal("Red Carpet", enDetail!.Name);
        Assert.Equal("سجادة حمراء", arDetail!.Name);

        // İngilizce çeviri hiç girilmemiş bir dil (ör. Almanca) istenirse ProductCode'a düşmeli -
        // hiçbir çeviri "kaybolmuş" gibi görünmemeli, sadece eksik olan boş/varsayılana düşer.
        var deDetail = await new GetProductBySlugQueryHandler(unitOfWork).Handle(new GetProductBySlugQuery("test-cokdil-urun", "de"), CancellationToken.None);
        Assert.Equal("COKDIL-001", deDetail!.Name);

        // Admin çeviri düzenleme ekranının kullandığı sorgu da (GetProductByIdQuery) aynı şekilde
        // her dili doğru döndürmeli - tek bir dile "sahip" değil, ÜÇÜNÜ birden taşıyor.
        var enAdminDetail = await new GetProductByIdQueryHandler(unitOfWork).Handle(new GetProductByIdQuery(productId, "en"), CancellationToken.None);
        Assert.Equal("Red Carpet", enAdminDetail!.Name);
        var trAdminDetail = await new GetProductByIdQueryHandler(unitOfWork).Handle(new GetProductByIdQuery(productId, "tr"), CancellationToken.None);
        Assert.Equal("Kırmızı Halı", trAdminDetail!.Name);
    }

    [Fact]
    public async Task KategoriyeIkinciBirDildeCeviriEklemekIlkDildekiniSilmez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var categoryId = await new CreateCategoryCommandHandler(unitOfWork).Handle(
            new CreateCategoryCommand("test-cokdil-kategori", null, 0, "tr", "Halılar", null), CancellationToken.None);

        await new UpdateCategoryCommandHandler(unitOfWork).Handle(
            new UpdateCategoryCommand(categoryId, "test-cokdil-kategori", null, "en", "Carpets", null), CancellationToken.None);

        var trCategory = await new GetCategoryByIdQueryHandler(unitOfWork).Handle(new GetCategoryByIdQuery(categoryId, "tr"), CancellationToken.None);
        var enCategory = await new GetCategoryByIdQueryHandler(unitOfWork).Handle(new GetCategoryByIdQuery(categoryId, "en"), CancellationToken.None);

        Assert.Equal("Halılar", trCategory!.Name);
        Assert.Equal("Carpets", enCategory!.Name);
    }
}
