using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - ürün değerlendirme/soru-cevap moderasyon akışının
/// (onay bekleyen → onaylanmış/reddedilmiş, yanıt bekleyen → yanıtlanmış) doğru filtrelendiğini ve
/// ortalama puan hesabının (EF LINQ Average çevirisi) doğru çalıştığını kanıtlar.</summary>
public sealed class ProductFeedbackRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasProductFeedbackTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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

    private static async Task<(Guid ProductId, Guid Customer1Id, Guid Customer2Id, string Identity1, string Identity2)> SeedProductAndCustomersAsync(ApplicationDbContext dbContext, UnitOfWork unitOfWork)
    {
        var group = new CustomerGroup("Bireysel");
        dbContext.CustomerGroups.Add(group);
        await dbContext.SaveChangesAsync();

        var customer1 = new Customer("identity-1", "Ahmet Yılmaz", "ahmet@test.com", group.Id);
        var customer2 = new Customer("identity-2", "Ayşe Kaya", "ayse@test.com", group.Id);
        dbContext.Set<Customer>().AddRange(customer1, customer2);
        await dbContext.SaveChangesAsync();

        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-degerlendirme-urun", "DEGERLENDIRME-001", 300m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Test Ürün", Description: null), CancellationToken.None);
        // GetProductBySlugQuery yalnızca Aktif ürünleri döndürür - ortalama puan testinin bu sorguyu
        // kullanabilmesi için ürün yayınlanmış olmalı.
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);

        return (productId, customer1.Id, customer2.Id, "identity-1", "identity-2");
    }

    [Fact]
    public async Task DegerlendirmeGonderilince_OnayBekler_OnaylaninCaOrtalamaPuanaKatilir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (productId, _, _, identity1, identity2) = await SeedProductAndCustomersAsync(dbContext, unitOfWork);

        var reviewId = await new SubmitProductReviewCommandHandler(unitOfWork)
            .Handle(new SubmitProductReviewCommand(productId, identity1, 5, "Çok memnun kaldım."), CancellationToken.None);

        // Onaydan ÖNCE: storefront'un OnlyApproved sorgusunda görünmemeli.
        var approvedBefore = await new GetProductReviewsQueryHandler(unitOfWork)
            .Handle(new GetProductReviewsQuery(productId, OnlyApproved: true), CancellationToken.None);
        Assert.Empty(approvedBefore);

        // Ama admin moderasyon kuyruğunda görünmeli.
        var pending = await new GetPendingProductReviewsQueryHandler(unitOfWork).Handle(new GetPendingProductReviewsQuery(), CancellationToken.None);
        Assert.Contains(pending, p => p.Id == reviewId);

        await new ApproveProductReviewCommandHandler(unitOfWork).Handle(new ApproveProductReviewCommand(reviewId), CancellationToken.None);

        var approvedAfter = await new GetProductReviewsQueryHandler(unitOfWork)
            .Handle(new GetProductReviewsQuery(productId, OnlyApproved: true), CancellationToken.None);
        Assert.Single(approvedAfter);
        Assert.Equal("Ahmet Yılmaz", approvedAfter.Single().CustomerName);

        // İkinci bir müşteri 3 puan verip onaylanınca ortalama (5+3)/2 = 4 olmalı - GetProductBySlugQuery'nin
        // EF LINQ Average() çevirisini gerçek SQL Server'a karşı doğrular.
        var review2Id = await new SubmitProductReviewCommandHandler(unitOfWork)
            .Handle(new SubmitProductReviewCommand(productId, identity2, 3, "Fena değil."), CancellationToken.None);
        await new ApproveProductReviewCommandHandler(unitOfWork).Handle(new ApproveProductReviewCommand(review2Id), CancellationToken.None);

        var slugHandler = new Dekorras.Application.Catalog.Storefront.GetProductBySlugQueryHandler(unitOfWork);
        var productDetail = await slugHandler.Handle(new Dekorras.Application.Catalog.Storefront.GetProductBySlugQuery("test-degerlendirme-urun", "tr"), CancellationToken.None);
        Assert.NotNull(productDetail);
        Assert.Equal(2, productDetail!.ReviewCount);
        Assert.Equal(4m, productDetail.AverageRating);
    }

    [Fact]
    public async Task AyniMusteri_AyniUruneIkinciKezDegerlendirmeGonderemez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (productId, _, _, identity1, _) = await SeedProductAndCustomersAsync(dbContext, unitOfWork);

        await new SubmitProductReviewCommandHandler(unitOfWork)
            .Handle(new SubmitProductReviewCommand(productId, identity1, 4, "İlk değerlendirme."), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SubmitProductReviewCommandHandler(unitOfWork)
            .Handle(new SubmitProductReviewCommand(productId, identity1, 2, "İkinci deneme."), CancellationToken.None));
    }

    [Fact]
    public async Task ReddedilenDegerlendirme_StorefrontVeModerasyonKuyrugundaGorunmez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (productId, _, _, identity1, _) = await SeedProductAndCustomersAsync(dbContext, unitOfWork);

        var reviewId = await new SubmitProductReviewCommandHandler(unitOfWork)
            .Handle(new SubmitProductReviewCommand(productId, identity1, 1, "Kötü bir deneyimdi."), CancellationToken.None);
        await new RejectProductReviewCommandHandler(unitOfWork).Handle(new RejectProductReviewCommand(reviewId), CancellationToken.None);

        var approved = await new GetProductReviewsQueryHandler(unitOfWork)
            .Handle(new GetProductReviewsQuery(productId, OnlyApproved: true), CancellationToken.None);
        Assert.Empty(approved);

        var pending = await new GetPendingProductReviewsQueryHandler(unitOfWork).Handle(new GetPendingProductReviewsQuery(), CancellationToken.None);
        Assert.DoesNotContain(pending, p => p.Id == reviewId);
    }

    [Fact]
    public async Task SoruSorulunca_YanitBekler_YanitlaninCaStorefrontaGorunur()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var (productId, _, _, identity1, _) = await SeedProductAndCustomersAsync(dbContext, unitOfWork);

        var questionId = await new SubmitProductQuestionCommandHandler(unitOfWork)
            .Handle(new SubmitProductQuestionCommand(productId, identity1, "Bu ürün yıkanabilir mi?"), CancellationToken.None);

        var answeredBefore = await new GetProductQuestionsQueryHandler(unitOfWork)
            .Handle(new GetProductQuestionsQuery(productId, OnlyAnswered: true), CancellationToken.None);
        Assert.Empty(answeredBefore);

        var pending = await new GetUnansweredProductQuestionsQueryHandler(unitOfWork).Handle(new GetUnansweredProductQuestionsQuery(), CancellationToken.None);
        Assert.Contains(pending, q => q.Id == questionId);

        await new AnswerProductQuestionCommandHandler(unitOfWork).Handle(new AnswerProductQuestionCommand(questionId, "Evet, 30 derecede yıkanabilir."), CancellationToken.None);

        var answeredAfter = await new GetProductQuestionsQueryHandler(unitOfWork)
            .Handle(new GetProductQuestionsQuery(productId, OnlyAnswered: true), CancellationToken.None);
        Assert.Single(answeredAfter);
        Assert.Equal("Evet, 30 derecede yıkanabilir.", answeredAfter.Single().Answer);

        var pendingAfter = await new GetUnansweredProductQuestionsQueryHandler(unitOfWork).Handle(new GetUnansweredProductQuestionsQuery(), CancellationToken.None);
        Assert.DoesNotContain(pendingAfter, q => q.Id == questionId);
    }
}
