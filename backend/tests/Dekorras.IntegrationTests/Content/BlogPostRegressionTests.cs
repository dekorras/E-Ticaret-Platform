using Dekorras.Application.Content.Commands;
using Dekorras.Application.Content.Queries;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Content;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.Content.BlogPost` Faz 0/1'den beri vardı ama hiç
/// Application/UI katmanı yoktu. CmsPage'in aksine BlogPost'ta çoklu dil bir Translations alt koleksiyonu
/// ile DEĞİL, aynı slug'ın farklı dillerde AYRI satırlar olması ile sağlanır.</summary>
public sealed class BlogPostRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasBlogPostTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task YaziOlusturulurTaslakOlarakStorefrontaGorunmezYayinlaninCaGorunur()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var postId = await new CreateBlogPostCommandHandler(unitOfWork).Handle(new CreateBlogPostCommand(
            "kartonpiyer-secim-rehberi", "tr", "Kartonpiyer Seçim Rehberi", "<p>Taslak içerik.</p>"), CancellationToken.None);

        // Yeni oluşturulan yazı varsayılan olarak TASLAK - Storefront'ta görünmemeli.
        var beforePublish = await new GetBlogPostBySlugQueryHandler(unitOfWork)
            .Handle(new GetBlogPostBySlugQuery("kartonpiyer-secim-rehberi", "tr"), CancellationToken.None);
        Assert.Null(beforePublish);

        // Ama Admin listesinde durumdan bağımsız görünmeli.
        var adminDetail = await new GetBlogPostByIdQueryHandler(unitOfWork).Handle(new GetBlogPostByIdQuery(postId), CancellationToken.None);
        Assert.NotNull(adminDetail);
        Assert.False(adminDetail!.IsPublished);

        await new SetBlogPostPublishedCommandHandler(unitOfWork).Handle(new SetBlogPostPublishedCommand(postId, true), CancellationToken.None);

        var afterPublish = await new GetBlogPostBySlugQueryHandler(unitOfWork)
            .Handle(new GetBlogPostBySlugQuery("kartonpiyer-secim-rehberi", "tr"), CancellationToken.None);
        Assert.NotNull(afterPublish);
        Assert.Equal("Kartonpiyer Seçim Rehberi", afterPublish!.Title);

        await new UpdateBlogPostCommandHandler(unitOfWork).Handle(new UpdateBlogPostCommand(
            postId, "Kartonpiyer Seçim Rehberi (Güncel)", "<p>Güncel içerik.</p>"), CancellationToken.None);

        var afterUpdate = await new GetBlogPostBySlugQueryHandler(unitOfWork)
            .Handle(new GetBlogPostBySlugQuery("kartonpiyer-secim-rehberi", "tr"), CancellationToken.None);
        Assert.Equal("Kartonpiyer Seçim Rehberi (Güncel)", afterUpdate!.Title);

        await new SetBlogPostPublishedCommandHandler(unitOfWork).Handle(new SetBlogPostPublishedCommand(postId, false), CancellationToken.None);

        var afterUnpublish = await new GetBlogPostBySlugQueryHandler(unitOfWork)
            .Handle(new GetBlogPostBySlugQuery("kartonpiyer-secim-rehberi", "tr"), CancellationToken.None);
        Assert.Null(afterUnpublish); // yayından kaldırılan yazı Storefront'ta artık görünmemeli
    }

    [Fact]
    public async Task AyniSlugFarkliDildeAyriKayitOlarakOlusturulabilir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var trId = await new CreateBlogPostCommandHandler(unitOfWork).Handle(new CreateBlogPostCommand(
            "kartonpiyer-secim-rehberi", "tr", "Kartonpiyer Seçim Rehberi", "<p>TR içerik.</p>"), CancellationToken.None);

        // CmsPage'in çeviri-upsert deseninin AKSİNE, aynı slug farklı bir dilde YENİ bir kayıt oluşturur.
        var enId = await new CreateBlogPostCommandHandler(unitOfWork).Handle(new CreateBlogPostCommand(
            "kartonpiyer-secim-rehberi", "en", "Cornice Selection Guide", "<p>EN content.</p>"), CancellationToken.None);

        Assert.NotEqual(trId, enId);

        await new SetBlogPostPublishedCommandHandler(unitOfWork).Handle(new SetBlogPostPublishedCommand(trId, true), CancellationToken.None);
        await new SetBlogPostPublishedCommandHandler(unitOfWork).Handle(new SetBlogPostPublishedCommand(enId, true), CancellationToken.None);

        var trPost = await new GetBlogPostBySlugQueryHandler(unitOfWork).Handle(new GetBlogPostBySlugQuery("kartonpiyer-secim-rehberi", "tr"), CancellationToken.None);
        var enPost = await new GetBlogPostBySlugQueryHandler(unitOfWork).Handle(new GetBlogPostBySlugQuery("kartonpiyer-secim-rehberi", "en"), CancellationToken.None);
        Assert.Equal("Kartonpiyer Seçim Rehberi", trPost!.Title);
        Assert.Equal("Cornice Selection Guide", enPost!.Title);

        // Aynı (Slug, LanguageCode) kombinasyonu ile ikinci bir kayıt oluşturulamaz.
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateBlogPostCommandHandler(unitOfWork)
            .Handle(new CreateBlogPostCommand("kartonpiyer-secim-rehberi", "tr", "Başka Başlık", "<p>İçerik.</p>"), CancellationToken.None));
    }
}
