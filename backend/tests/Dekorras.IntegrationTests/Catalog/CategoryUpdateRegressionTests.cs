using Dekorras.Application.Catalog.Commands;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>
/// Gerçek SQL Server'a karşı çalışır. Her adım BİLİNÇLİ olarak kendi taze DbContext'ini kullanır -
/// tıpkı gerçek hayatta her HTTP isteğinin kendi scoped DbContext'i olduğu gibi. Aynı DbContext
/// tekrar kullanılsaydı, EF Core'un identity map'i entity'yi zaten "yüklü" tutar ve bu test
/// yakalamaya çalıştığı hatayı (Include olmadan navigation'a dokunma) maskeleyip yanlışlıkla
/// yeşil geçerdi. NSubstitute mock'ları da bu sınıf hatayı YAKALAYAMAZ, çünkü sahte repository
/// her zaman "tam yüklü" davranır - bu yüzden bu regresyon testi gerçek bir DB gerektirir.
/// </summary>
public sealed class CategoryUpdateRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasIntegrationTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task IkinciKezGuncelleme_AyniDilIcinYinelenenCeviriSatiriOlusturmaz()
    {
        Guid categoryId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var category = new Category("test-regresyon", null, 0);
            category.SetTranslation("tr", "İlk Ad", null, null, null);
            await unitOfWork.Repository<Category>().AddAsync(category, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            categoryId = category.Id;
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new UpdateCategoryCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new UpdateCategoryCommand(categoryId, "test-regresyon", null, "tr", "İkinci Ad", null), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new UpdateCategoryCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new UpdateCategoryCommand(categoryId, "test-regresyon", null, "tr", "Üçüncü Ad", null), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var translations = await verifyContext.Set<CategoryTranslation>()
            .Where(t => t.CategoryId == categoryId && t.LanguageCode == "tr")
            .ToListAsync();

        Assert.Single(translations);
        Assert.Equal("Üçüncü Ad", translations[0].Name);
    }

    [Fact]
    public async Task VarOlanKategoriyeYeniBirDilEklemek_MevcutCeviriyiSilmezVeHataVermez()
    {
        Guid categoryId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var category = new Category("test-regresyon-2", null, 0);
            category.SetTranslation("tr", "Türkçe Ad", null, null, null);
            await unitOfWork.Repository<Category>().AddAsync(category, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            categoryId = category.Id;
        }

        // "en" çevirisi bu kategori için İLK KEZ ekleniyor - Update(category) çağrılsaydı bu
        // yeni CategoryTranslation'ı "Modified" sanıp DbUpdateConcurrencyException fırlatırdı.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new UpdateCategoryCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new UpdateCategoryCommand(categoryId, "test-regresyon-2", null, "en", "English Name", null), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var translations = await verifyContext.Set<CategoryTranslation>()
            .Where(t => t.CategoryId == categoryId)
            .ToListAsync();

        Assert.Equal(2, translations.Count);
        Assert.Contains(translations, t => t.LanguageCode == "tr" && t.Name == "Türkçe Ad");
        Assert.Contains(translations, t => t.LanguageCode == "en" && t.Name == "English Name");
    }
}
