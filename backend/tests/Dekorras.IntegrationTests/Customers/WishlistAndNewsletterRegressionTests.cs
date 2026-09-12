using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Customers.Commands;
using Dekorras.Application.Customers.Queries;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Customers;

/// <summary>Gerçek SQL Server'a karşı çalışır - favoriler (Wishlist) ve bülten aboneliğinin
/// (`Customer.AddToWishlist`/`SubscribeNewsletter` Faz 0/1'den beri domain'de vardı, hiç Application
/// katmanı yoktu) doğru çalıştığını kanıtlar.</summary>
public sealed class WishlistAndNewsletterRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasWishlistTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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

    private static async Task<(Guid ProductId, string IdentityUserId)> SeedCustomerAndProductAsync(ApplicationDbContext dbContext, UnitOfWork unitOfWork)
    {
        var group = new CustomerGroup("Bireysel");
        dbContext.CustomerGroups.Add(group);
        await dbContext.SaveChangesAsync();

        var customer = new Customer("identity-wishlist-1", "Favori Müşterisi", "favori@test.com", group.Id);
        dbContext.Set<Customer>().Add(customer);
        await dbContext.SaveChangesAsync();

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-wishlist-urun", "WISHLIST-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null), CancellationToken.None);

        return (productId, customer.IdentityUserId);
    }

    [Fact]
    public async Task UrunFavorilereEklenirVeCikarilir_IkinciKezEklemekYinelenenSatirOlusturmaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (productId, identityUserId) = await SeedCustomerAndProductAsync(dbContext, unitOfWork);

        await new AddToWishlistCommandHandler(unitOfWork).Handle(new AddToWishlistCommand(identityUserId, productId), CancellationToken.None);

        var wishlist = await new GetMyWishlistQueryHandler(unitOfWork).Handle(new GetMyWishlistQuery(identityUserId, "tr"), CancellationToken.None);
        Assert.Single(wishlist);
        Assert.Equal("Test Ürün", wishlist.Single().Name);

        var isInWishlist = await new IsInWishlistQueryHandler(unitOfWork).Handle(new IsInWishlistQuery(identityUserId, productId), CancellationToken.None);
        Assert.True(isInWishlist);

        // Aynı ürünü İKİNCİ kez eklemek (ör. sayfayı yenileyip tekrar tıklamak) yinelenen bir satır
        // OLUŞTURMAMALI - bu, Customer.AddToWishlist'in zaten var olup olmadığını kontrol eden
        // korumasının gerçek bir regresyon senaryosu.
        await new AddToWishlistCommandHandler(unitOfWork).Handle(new AddToWishlistCommand(identityUserId, productId), CancellationToken.None);
        var wishlistAfterSecondAdd = await new GetMyWishlistQueryHandler(unitOfWork).Handle(new GetMyWishlistQuery(identityUserId, "tr"), CancellationToken.None);
        Assert.Single(wishlistAfterSecondAdd);

        await new RemoveFromWishlistCommandHandler(unitOfWork).Handle(new RemoveFromWishlistCommand(identityUserId, productId), CancellationToken.None);
        var wishlistAfterRemove = await new GetMyWishlistQueryHandler(unitOfWork).Handle(new GetMyWishlistQuery(identityUserId, "tr"), CancellationToken.None);
        Assert.Empty(wishlistAfterRemove);

        var isInWishlistAfterRemove = await new IsInWishlistQueryHandler(unitOfWork).Handle(new IsInWishlistQuery(identityUserId, productId), CancellationToken.None);
        Assert.False(isInWishlistAfterRemove);
    }

    [Fact]
    public async Task BultenAbonelikDurumuDegistirilebilir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (_, identityUserId) = await SeedCustomerAndProductAsync(dbContext, unitOfWork);

        var profileBefore = await new GetMyCustomerProfileQueryHandler(unitOfWork).Handle(new GetMyCustomerProfileQuery(identityUserId), CancellationToken.None);
        Assert.False(profileBefore!.NewsletterSubscribed);

        await new SetNewsletterSubscriptionCommandHandler(unitOfWork).Handle(new SetNewsletterSubscriptionCommand(identityUserId, true), CancellationToken.None);
        var profileAfterSubscribe = await new GetMyCustomerProfileQueryHandler(unitOfWork).Handle(new GetMyCustomerProfileQuery(identityUserId), CancellationToken.None);
        Assert.True(profileAfterSubscribe!.NewsletterSubscribed);

        await new SetNewsletterSubscriptionCommandHandler(unitOfWork).Handle(new SetNewsletterSubscriptionCommand(identityUserId, false), CancellationToken.None);
        var profileAfterUnsubscribe = await new GetMyCustomerProfileQueryHandler(unitOfWork).Handle(new GetMyCustomerProfileQuery(identityUserId), CancellationToken.None);
        Assert.False(profileAfterUnsubscribe!.NewsletterSubscribed);
    }
}
