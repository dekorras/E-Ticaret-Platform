using Dekorras.Application.Content.Commands;
using Dekorras.Application.Content.Queries;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Content;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.Content.CmsPage` Faz 0/1'den beri vardı
/// (plan §4 6 statik yasal/kurumsal sayfa istiyor: Gizlilik Politikası, Hakkımızda, vb.) ama hiç
/// Application/UI katmanı yoktu. Storefront'un footer'daki gerçek Gizlilik Politikası bağlantısı
/// bu turdan önce ASP.NET'in scaffold placeholder metnine ("Use this page to detail...") gidiyordu.</summary>
public sealed class CmsPageRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCmsPageTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task SayfaOlusturulurGuncellenirVePasifeAlinaninCaStorefrontta404Doner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var pageId = await new CreateCmsPageCommandHandler(unitOfWork).Handle(new CreateCmsPageCommand(
            "gizlilik-politikasi", "tr", "Gizlilik Politikası", "<p>Taslak içerik.</p>"), CancellationToken.None);

        // Storefront yalnızca Aktif sayfaları döndürür - yeni oluşturulan sayfa varsayılan olarak Aktif.
        var storefrontPage = await new GetCmsPageBySlugQueryHandler(unitOfWork)
            .Handle(new GetCmsPageBySlugQuery("gizlilik-politikasi", "tr"), CancellationToken.None);
        Assert.NotNull(storefrontPage);
        Assert.Equal("Gizlilik Politikası", storefrontPage!.Title);

        // İkinci bir dilde çeviri eklemek ilk dildekini SİLMEMELİ (bkz. SetTranslation upsert deseni).
        await new UpdateCmsPageCommandHandler(unitOfWork).Handle(new UpdateCmsPageCommand(
            pageId, "gizlilik-politikasi", "en", "Privacy Policy", "<p>Draft content.</p>"), CancellationToken.None);

        var trPage = await new GetCmsPageBySlugQueryHandler(unitOfWork).Handle(new GetCmsPageBySlugQuery("gizlilik-politikasi", "tr"), CancellationToken.None);
        var enPage = await new GetCmsPageBySlugQueryHandler(unitOfWork).Handle(new GetCmsPageBySlugQuery("gizlilik-politikasi", "en"), CancellationToken.None);
        Assert.Equal("Gizlilik Politikası", trPage!.Title);
        Assert.Equal("Privacy Policy", enPage!.Title);

        await new SetCmsPageActiveCommandHandler(unitOfWork).Handle(new SetCmsPageActiveCommand(pageId, false), CancellationToken.None);

        var pageAfterDeactivate = await new GetCmsPageBySlugQueryHandler(unitOfWork)
            .Handle(new GetCmsPageBySlugQuery("gizlilik-politikasi", "tr"), CancellationToken.None);
        Assert.Null(pageAfterDeactivate); // pasif sayfa Storefront'ta artık HİÇ görünmemeli (404)

        // Ama Admin'in kendi sorgusu (GetCmsPageByIdQuery) durumdan bağımsız hâlâ dönmeli - aksi
        // halde admin pasife aldığı bir sayfayı bir daha ASLA düzenleyemezdi.
        var adminDetail = await new GetCmsPageByIdQueryHandler(unitOfWork).Handle(new GetCmsPageByIdQuery(pageId, "tr"), CancellationToken.None);
        Assert.NotNull(adminDetail);
    }

    [Fact]
    public async Task AyniSlugIleIkinciSayfaOlusturulamaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        await new CreateCmsPageCommandHandler(unitOfWork).Handle(new CreateCmsPageCommand(
            "hakkimizda", "tr", "Hakkımızda", "<p>İçerik.</p>"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateCmsPageCommandHandler(unitOfWork)
            .Handle(new CreateCmsPageCommand("hakkimizda", "tr", "Hakkımızda 2", "<p>İçerik.</p>"), CancellationToken.None));
    }
}
