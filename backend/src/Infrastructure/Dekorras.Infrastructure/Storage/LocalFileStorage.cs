using Dekorras.Application.Common.Interfaces;

namespace Dekorras.Infrastructure.Storage;

/// <summary>
/// TODO: production'da Azure Blob Storage veya S3 uyumlu (MinIO) depolamayla değiştirilecek
/// (bkz. plan §5). Geliştirme ortamında paylaşılan bir klasöre yazar.
/// </summary>
public sealed class LocalFileStorage(string rootPath) : IFileStorage
{
    /// <summary>
    /// Dekorras.Admin (yükleyen), Dekorras.Api ve Dekorras.Storefront (gösteren) AYRI süreçlerdir;
    /// her biri kendi `AppContext.BaseDirectory`'sine yazsaydı, Admin'de yüklenen bir ürün görseli
    /// Storefront'ta hiç görünmezdi. Bu yüzden ÜÇÜ DE aynı paylaşılan fiziksel klasörü kullanır
    /// (bkz. Infrastructure/DependencyInjection.cs kaydı ve her Presentation projesinin Program.cs'i
    /// - `app.UseStaticFiles` burayı `/uploads` altında sunmalıdır). Production'da bunun yerine
    /// gerçek bir Blob/S3 deposu kullanılmalıdır (yukarıdaki TODO).
    /// </summary>
    public static readonly string SharedUploadsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dekorras", "uploads");


    public async Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var containerPath = Path.Combine(rootPath, containerName);
        Directory.CreateDirectory(containerPath);

        var uniqueFileName = $"{Guid.NewGuid():N}-{fileName}";
        var filePath = Path.Combine(containerPath, uniqueFileName);

        await using var fileStream = File.Create(filePath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return $"/uploads/{containerName}/{uniqueFileName}";
    }

    public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken)
    {
        var relativePath = fileUrl.TrimStart('/').Replace("uploads/", string.Empty);
        var filePath = Path.Combine(rootPath, relativePath);
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }
}
