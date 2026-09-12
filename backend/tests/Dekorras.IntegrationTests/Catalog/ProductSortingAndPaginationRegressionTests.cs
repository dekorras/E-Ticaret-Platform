using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - plan §2.1'in "ürün listeleme sayfası: sıralama
/// (varsayılan, ad A-Z/Z-A, ucuzdan-pahalıya, pahalıdan-ucuza, puana göre, ürün koduna göre), sayfa
/// başına gösterim (12/25/50/75/100)" maddesi Faz 0/1'den beri hiç yazılmamıştı (orphaned bir
/// entity DEĞİL, tamamen eksik bir özellikti). "Puana göre" sıralama özellikle önemli - approved
/// review ortalamasının bir korelasyonlu alt sorgu (`Average`) olarak SQL'e GERÇEKTEN çevrildiğini
/// kanıtlar, bu sınıf hata NSubstitute mock'larıyla asla yakalanamaz.</summary>
public sealed class ProductSortingAndPaginationRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasSortPaginationTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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

    private static async Task<(Guid Cheap, Guid Mid, Guid Expensive)> SeedThreeProductsAsync(UnitOfWork unitOfWork)
    {
        var handler = new CreateProductCommandHandler(unitOfWork);
        var publishHandler = new SetProductPublishedCommandHandler(unitOfWork);

        var cheapId = await handler.Handle(new CreateProductCommand(
            "test-sort-ucuz", "SORT-A", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Alfa Ürün", Description: null), CancellationToken.None);
        await publishHandler.Handle(new SetProductPublishedCommand(cheapId, Published: true), CancellationToken.None);

        var midId = await handler.Handle(new CreateProductCommand(
            "test-sort-orta", "SORT-B", 200m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Beta Ürün", Description: null), CancellationToken.None);
        await publishHandler.Handle(new SetProductPublishedCommand(midId, Published: true), CancellationToken.None);

        var expensiveId = await handler.Handle(new CreateProductCommand(
            "test-sort-pahali", "SORT-C", 300m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Gama Ürün", Description: null), CancellationToken.None);
        await publishHandler.Handle(new SetProductPublishedCommand(expensiveId, Published: true), CancellationToken.None);

        return (cheapId, midId, expensiveId);
    }

    [Fact]
    public async Task FiyataGoreArtanVeAzalanSiralamaDogruCalisir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (cheapId, _, expensiveId) = await SeedThreeProductsAsync(unitOfWork);

        var queryHandler = new GetStorefrontProductsQueryHandler(unitOfWork);

        var ascending = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SortBy: ProductSortOrder.PriceAsc), CancellationToken.None);
        Assert.Equal(cheapId, ascending.Items.First().Id);
        Assert.Equal(expensiveId, ascending.Items.Last().Id);

        var descending = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SortBy: ProductSortOrder.PriceDesc), CancellationToken.None);
        Assert.Equal(expensiveId, descending.Items.First().Id);
        Assert.Equal(cheapId, descending.Items.Last().Id);
    }

    [Fact]
    public async Task AdaGoreSiralamaVeUrunKoduSiralamasiDogruCalisir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (cheapId, _, expensiveId) = await SeedThreeProductsAsync(unitOfWork); // Alfa(ucuz)/Beta(orta)/Gama(pahalı)

        var queryHandler = new GetStorefrontProductsQueryHandler(unitOfWork);

        // Ad A-Z: Alfa, Beta, Gama -> ucuz ürün ("Alfa") ilk sırada.
        var nameAsc = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SortBy: ProductSortOrder.NameAsc), CancellationToken.None);
        Assert.Equal(cheapId, nameAsc.Items.First().Id);

        // Ürün koduna göre: SORT-A, SORT-B, SORT-C -> yine ucuz ürün ilk sırada.
        var byCode = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SortBy: ProductSortOrder.ProductCode), CancellationToken.None);
        Assert.Equal(cheapId, byCode.Items.First().Id);
        Assert.Equal(expensiveId, byCode.Items.Last().Id);
    }

    [Fact]
    public async Task PuanaGoreSiralamaOnaylanmisYorumOrtalamasiniKullanir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (lowRatedId, _, highRatedId) = await SeedThreeProductsAsync(unitOfWork);

        var customerGroup = new CustomerGroup("Bireysel");
        dbContext.CustomerGroups.Add(customerGroup);
        await dbContext.SaveChangesAsync();
        var reviewer = new Customer("identity-sort-reviewer", "Test Değerlendirici", "reviewer@test.com", customerGroup.Id);
        dbContext.Set<Customer>().Add(reviewer);
        await dbContext.SaveChangesAsync();

        // Düşük puanlı ürüne 2 yıldız, yüksek puanlıya 5 yıldız onaylanmış yorum ekle - üçüncü ürünün
        // (midId) hiç yorumu yok, ortalaması 0 olarak değerlendirilmeli (sona düşmeli).
        var lowReviewId = await new SubmitProductReviewCommandHandler(unitOfWork)
            .Handle(new SubmitProductReviewCommand(lowRatedId, reviewer.IdentityUserId, 2, "Fena değil"), CancellationToken.None);
        await new ApproveProductReviewCommandHandler(unitOfWork).Handle(new ApproveProductReviewCommand(lowReviewId), CancellationToken.None);

        var highReviewId = await new SubmitProductReviewCommandHandler(unitOfWork)
            .Handle(new SubmitProductReviewCommand(highRatedId, reviewer.IdentityUserId, 5, "Harika"), CancellationToken.None);
        await new ApproveProductReviewCommandHandler(unitOfWork).Handle(new ApproveProductReviewCommand(highReviewId), CancellationToken.None);

        var queryHandler = new GetStorefrontProductsQueryHandler(unitOfWork);
        var byRating = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, SortBy: ProductSortOrder.Rating), CancellationToken.None);

        Assert.Equal(highRatedId, byRating.Items.First().Id);
        Assert.Equal(lowRatedId, byRating.Items.Skip(1).First().Id);
    }

    [Fact]
    public async Task SayfalamaToplamSayimiKorurVeDogruDilimiDoner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        await SeedThreeProductsAsync(unitOfWork);

        var queryHandler = new GetStorefrontProductsQueryHandler(unitOfWork);

        var firstPage = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, Page: 1, PageSize: 2, SortBy: ProductSortOrder.ProductCode), CancellationToken.None);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);

        var secondPage = await queryHandler.Handle(new GetStorefrontProductsQuery("tr", CategoryId: null, Page: 2, PageSize: 2, SortBy: ProductSortOrder.ProductCode), CancellationToken.None);
        Assert.Single(secondPage.Items);
        Assert.Equal(3, secondPage.TotalCount);
    }
}
