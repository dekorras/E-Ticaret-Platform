using Dekorras.Application.Catalog.Commands;
using Dekorras.Domain.Catalog;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>
/// Gerçek SQL Server'a karşı çalışır. Her adım kendi taze DbContext'ini kullanır (bkz.
/// CategoryUpdateRegressionTests'teki aynı notu) - aksi halde EF Core'un identity map'i entity'yi
/// zaten "yüklü" tutup bu testin yakalamaya çalıştığı sınıf hatayı maskeler.
/// </summary>
public sealed class ProductImageRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasProductImageTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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

    private static Dekorras.Application.Common.Interfaces.IFileStorage CreateFileStorage() => new FileStorageStub();

    [Fact]
    public async Task IlkGorsel_OtomatikAnaGorselOlurVeIkinciGorselEklemekHataVermez()
    {
        Guid productId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var product = new Product("test-gorsel-urun", "IMG-001", 100m, 20m, UnitOfMeasure.Piece);
            await unitOfWork.Repository<Product>().AddAsync(product, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            productId = product.Id;
        }

        var fileStorage = CreateFileStorage();

        // İlk görsel: yeni bir Product'a (bu process için hâlâ "az önce oluşturuldu" değil, TAZE
        // bir DbContext ile fetch ediliyor) ekleniyor - Images koleksiyonu boş olduğundan otomatik
        // ana görsel olmalı.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductImageCommandHandler(new UnitOfWork(dbContext), fileStorage);
            using var stream1 = new MemoryStream([1, 2, 3]);
            await handler.Handle(new AddProductImageCommand(productId, stream1, "gorsel1.jpg", "image/jpeg"), CancellationToken.None);
        }

        // İkinci görsel: Product artık ZATEN bir görsele sahip - bu, Images koleksiyonunun
        // LoadCollectionAsync ile doğru yüklenip yeni görselin "Modified" değil "Added" olarak
        // işaretlendiğini doğrulayan asıl regresyon senaryosu.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductImageCommandHandler(new UnitOfWork(dbContext), fileStorage);
            using var stream2 = new MemoryStream([4, 5, 6]);
            await handler.Handle(new AddProductImageCommand(productId, stream2, "gorsel2.jpg", "image/jpeg"), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var images = await verifyContext.Set<ProductImage>().Where(i => i.ProductId == productId).OrderBy(i => i.DisplayOrder).ToListAsync();

        Assert.Equal(2, images.Count);
        Assert.True(images[0].IsPrimary);
        Assert.False(images[1].IsPrimary);
    }

    [Fact]
    public async Task AnaGorselSilinince_KalanIlkGorselOtomatikAnaGorselOlur()
    {
        Guid productId;
        var fileStorage = CreateFileStorage();

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var product = new Product("test-gorsel-urun-2", "IMG-002", 100m, 20m, UnitOfMeasure.Piece);
            await unitOfWork.Repository<Product>().AddAsync(product, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            productId = product.Id;
        }

        Guid firstImageId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductImageCommandHandler(new UnitOfWork(dbContext), fileStorage);
            using var stream1 = new MemoryStream([1]);
            firstImageId = await handler.Handle(new AddProductImageCommand(productId, stream1, "a.jpg", "image/jpeg"), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddProductImageCommandHandler(new UnitOfWork(dbContext), fileStorage);
            using var stream2 = new MemoryStream([2]);
            await handler.Handle(new AddProductImageCommand(productId, stream2, "b.jpg", "image/jpeg"), CancellationToken.None);
        }

        // Ana görseli (ilk eklenen) sil - kalan görsel otomatik ana görsel olmalı.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RemoveProductImageCommandHandler(new UnitOfWork(dbContext), fileStorage);
            await handler.Handle(new RemoveProductImageCommand(productId, firstImageId), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var remaining = await verifyContext.Set<ProductImage>().Where(i => i.ProductId == productId).ToListAsync();

        Assert.Single(remaining);
        Assert.True(remaining[0].IsPrimary);
    }
}

/// <summary>Gerçek disk G/Ç'sinden kaçınmak için testlerde kullanılan sahte dosya deposu.</summary>
file sealed class FileStorageStub : Dekorras.Application.Common.Interfaces.IFileStorage
{
    public Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken) =>
        Task.FromResult($"/uploads/{containerName}/{Guid.NewGuid():N}-{fileName}");

    public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken) => Task.CompletedTask;
}
