using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>
/// Gerçek SQL Server'a karşı çalışır. Her adım kendi taze DbContext'ini kullanır (bkz.
/// CategoryUpdateRegressionTests'teki aynı not) - bu, ikinci bir varyant eklemenin/güncellemenin/
/// silmenin var olan Variants koleksiyonunu doğru yüklediğini (LoadCollectionAsync) ve
/// DbUpdateConcurrencyException fırlatmadığını kanıtlar.
/// </summary>
public sealed class ProductVariantRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasProductVariantTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task IkinciVaryantEklemekVeGuncellemekHataVermez()
    {
        Guid productId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var product = new Product("test-varyant-urun", "VAR-001", 100m, 20m, UnitOfMeasure.Piece);
            await unitOfWork.Repository<Product>().AddAsync(product, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            productId = product.Id;
        }

        Guid firstVariantId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductVariantCommandHandler(new UnitOfWork(dbContext));
            firstVariantId = await handler.Handle(new AddProductVariantCommand(productId, "VAR-001-S", "Ölçü: Küçük", 0m, 10), CancellationToken.None);
        }

        // İkinci varyant: Product artık ZATEN bir varyanta sahip - Variants koleksiyonunun doğru
        // yüklenip yeni varyantın "Modified" değil "Added" olarak işaretlendiğini doğrular.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductVariantCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new AddProductVariantCommand(productId, "VAR-001-L", "Ölçü: Büyük", 15m, 5), CancellationToken.None);
        }

        // Var olan bir varyantı güncelle - koleksiyonda BAŞKA bir varyant daha varken.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new UpdateProductVariantCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new UpdateProductVariantCommand(productId, firstVariantId, "Ölçü: Küçük (güncellendi)", 2m, 8), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var variants = await verifyContext.Set<ProductVariant>().Where(v => v.ProductId == productId).OrderBy(v => v.Sku).ToListAsync();

        Assert.Equal(2, variants.Count);
        var updated = variants.Single(v => v.Id == firstVariantId);
        Assert.Equal("Ölçü: Küçük (güncellendi)", updated.OptionName);
        Assert.Equal(2m, updated.PriceAdjustmentTry);
        Assert.Equal(8, updated.StockQuantity);
    }

    [Fact]
    public async Task VaryantSilinince_DigerVaryantEtkilenmez()
    {
        Guid productId, keepVariantId, removeVariantId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var product = new Product("test-varyant-urun-2", "VAR-002", 100m, 20m, UnitOfMeasure.Piece);
            await unitOfWork.Repository<Product>().AddAsync(product, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            productId = product.Id;
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductVariantCommandHandler(new UnitOfWork(dbContext));
            keepVariantId = await handler.Handle(new AddProductVariantCommand(productId, "VAR-002-A", "Renk: Kırmızı", null, 3), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductVariantCommandHandler(new UnitOfWork(dbContext));
            removeVariantId = await handler.Handle(new AddProductVariantCommand(productId, "VAR-002-B", "Renk: Mavi", null, 4), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new RemoveProductVariantCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new RemoveProductVariantCommand(productId, removeVariantId), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var remaining = await verifyContext.Set<ProductVariant>().Where(v => v.ProductId == productId).ToListAsync();

        Assert.Single(remaining);
        Assert.Equal(keepVariantId, remaining[0].Id);

        await using var queryContext = CreateDbContext();
        var queryHandler = new GetProductVariantsQueryHandler(new UnitOfWork(queryContext));
        var dtos = await queryHandler.Handle(new GetProductVariantsQuery(productId), CancellationToken.None);
        Assert.Single(dtos);
        Assert.Equal("VAR-002-A", dtos.Single().Sku);
    }
}
