using Dekorras.Application.WallCovering;
using Dekorras.Infrastructure.Storage;

namespace Dekorras.Infrastructure.WallCovering;

/// <summary>Yerel disk tabanlı <see cref="IWallImageStore"/>. Herkese açık kök = paylaşılan /uploads
/// klasörü; özel kök = aynı üst klasörde ama /uploads DIŞINDA "private" (UseStaticFiles ile sunulmaz).
/// Tüm yollar kök altına sabitlenir - "../" ile dışarı çıkılamaz.
/// VARSAYIM: S3 uyumlu depolama bu arayüzün ikinci bir uygulamasıyla eklenecek (LocalFileStorage'daki TODO ile aynı).</summary>
public sealed class WallImageStore : IWallImageStore
{
    /// <remarks>Varsayılan: App_Data/private (wwwroot DIŞINDA, sunulmaz) - bkz. <see cref="StoragePaths"/>.</remarks>
    public static string PrivateRoot => StoragePaths.PrivateRoot;

    private readonly string _publicRoot;
    private readonly string _privateRoot;

    public WallImageStore() : this(LocalFileStorage.SharedUploadsRoot, PrivateRoot) { }

    public WallImageStore(string publicRoot, string privateRoot)
    {
        _publicRoot = Path.GetFullPath(publicRoot);
        _privateRoot = Path.GetFullPath(privateRoot);
    }

    public async Task<string> SavePublicAsync(string relativePath, byte[] content, CancellationToken cancellationToken)
    {
        var path = Resolve(_publicRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return "/uploads/" + relativePath.Replace('\\', '/').TrimStart('/');
    }

    public async Task SavePrivateAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(_privateRoot, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Stream? OpenPublic(string url)
    {
        var path = PublicPath(url);
        return path is not null && File.Exists(path) ? File.OpenRead(path) : null;
    }

    public Stream? OpenPrivate(string key)
    {
        var path = Resolve(_privateRoot, key);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public bool PublicExists(string url) => PublicPath(url) is { } p && File.Exists(p);

    public Task DeletePublicAsync(string url, CancellationToken cancellationToken)
    {
        if (PublicPath(url) is { } p && File.Exists(p)) File.Delete(p);
        return Task.CompletedTask;
    }

    public Task DeletePrivateAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(_privateRoot, key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string? PublicPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var clean = url.Split('?', '#')[0];
        if (!clean.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return null;
        return Resolve(_publicRoot, Uri.UnescapeDataString(clean["/uploads/".Length..]));
    }

    private static string Resolve(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Geçersiz dosya yolu.");
        return full;
    }
}
