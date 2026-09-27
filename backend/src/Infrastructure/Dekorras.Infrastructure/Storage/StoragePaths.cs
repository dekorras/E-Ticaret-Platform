using Microsoft.Extensions.Configuration;

namespace Dekorras.Infrastructure.Storage;

/// <summary>Yüklenen dosyaların fiziksel klasörleri. Veritabanında disk yolu DEĞİL yalnızca web adresi tutulur
/// ("/uploads/product-images/x.jpg"); bu yüzden klasör taşınınca veritabanı değişmez, yalnız burası değişir.
///
/// - <see cref="UploadsRoot"/>: herkese açık dosyalar (ürün görselleri, türevler, sahneler) - "/uploads" adresinden sunulur.
///   Varsayılan: Storefront projesinin <c>wwwroot/uploads</c> klasörü.
/// - <see cref="PrivateRoot"/>: ASLA doğrudan sunulmayan dosyalar (üretim PDF'leri, müşteri ekleri, orijinal yüksek
///   çözünürlüklü görseller). Varsayılan: <c>App_Data/private</c> (wwwroot DIŞINDA - adresi bilen indiremez).
///
/// appsettings'te <c>Storage:UploadsPath</c> / <c>Storage:PrivatePath</c> ile değiştirilebilir; göreli yol içerik köküne
/// (proje/site klasörü) göre çözülür. Canlıda site klasörünün DIŞINDA kalıcı bir klasör önerilir (yayınlama sırasında
/// silinmesin), ör. <c>"UploadsPath": "D:\\DekorrasData\\uploads"</c>.
/// İçerik kökü bilinmeyen süreçlerde (entegrasyon testleri) eski paylaşılan klasör (%LOCALAPPDATA%\Dekorras) kullanılır.</summary>
public static class StoragePaths
{
    private static readonly string LegacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dekorras");

    public static string UploadsRoot { get; private set; } = Path.Combine(LegacyRoot, "uploads");

    public static string PrivateRoot { get; private set; } = Path.Combine(LegacyRoot, "private");

    public static void Configure(IConfiguration configuration)
    {
        // WebApplicationBuilder yapılandırması içerik kökünü "contentRoot" anahtarıyla taşır.
        var contentRoot = configuration["contentRoot"];
        UploadsRoot = Resolve(configuration["Storage:UploadsPath"], contentRoot, Path.Combine("wwwroot", "uploads"), UploadsRoot);
        PrivateRoot = Resolve(configuration["Storage:PrivatePath"], contentRoot, Path.Combine("App_Data", "private"), PrivateRoot);
    }

    private static string Resolve(string? configured, string? contentRoot, string defaultRelative, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot ?? AppContext.BaseDirectory, configured));
        return string.IsNullOrWhiteSpace(contentRoot) ? fallback : Path.GetFullPath(Path.Combine(contentRoot, defaultRelative));
    }
}
