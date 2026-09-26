using Dekorras.Domain.WallCovering;

namespace Dekorras.Application.WallCovering;

/// <summary>Duvar kağıdı görsellerinin depolanması. İki ayrı kök vardır: herkese açık türevler
/// (/uploads altında sunulur) ve orijinal yüksek çözünürlüklü görseller (web kökü DIŞINDA, asla
/// herkese açık URL'si olmaz - spec 1.11). Admin/Api/Storefront aynı fiziksel klasörleri paylaşır
/// (bkz. LocalFileStorage.SharedUploadsRoot).</summary>
public interface IWallImageStore
{
    /// <summary>Herkese açık dosya yazar, "/uploads/..." URL'si döner.</summary>
    Task<string> SavePublicAsync(string relativePath, byte[] content, CancellationToken cancellationToken);

    Task SavePrivateAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <summary>"/uploads/..." URL'sini veya özel anahtarı okumak için açar; yoksa null.</summary>
    Stream? OpenPublic(string url);
    Stream? OpenPrivate(string key);
    bool PublicExists(string url);
    Task DeletePublicAsync(string url, CancellationToken cancellationToken);
    Task DeletePrivateAsync(string key, CancellationToken cancellationToken);
}

public sealed record ImageDerivatives(
    int WidthPx,
    int HeightPx,
    int? Dpi,
    string ThumbUrl,
    string ListUrl,
    string PreviewUrl,
    string LqipBase64,
    IReadOnlyList<string> DominantColors);

public sealed record ImageInspection(bool IsValid, string? Error, int WidthPx, int HeightPx, string? Format, string Extension);

/// <summary>Türev görsel üretimi (spec 1.2): 400px thumb, 800px liste, 2000px filigranlı önizleme,
/// bulanık LQIP ve 3–5 baskın renk. Ayrıca yüklenen dosyanın sihirli bayt/biçim doğrulaması.</summary>
public interface IImageDerivativeService
{
    /// <summary>Dosyanın gerçekten JPG/PNG/WebP olduğunu içerikten (uzantıdan değil) doğrular.</summary>
    ImageInspection Inspect(Stream content);

    Task<ImageDerivatives> CreateDerivativesAsync(Stream original, string folder, string baseName, CancellationToken cancellationToken);

    /// <summary>Kullanıcı yüklemesini EXIF/meta verilerden arındırıp yeniden kodlar (JPEG), en uzun kenarı sınırlar.</summary>
    Task<(byte[] Content, int WidthPx, int HeightPx)> SanitizeAsync(Stream content, int maxLongEdgePx, CancellationToken cancellationToken);

    /// <summary>Kullanıcının oda fotoğrafından gölge haritası: duvar bölgesinin ortanca parlaklığına göre
    /// normalize edilmiş, bulanıklaştırılmış (duvar dokusu değil yalnızca ışık/gölge kalsın) gri ton PNG.</summary>
    Task<byte[]> CreateShadowMapAsync(byte[] photo, WallQuad wallQuad, byte[]? foregroundMaskPng, CancellationToken cancellationToken);

    /// <summary>Fırçayla boyanan maskeyi doğrular: PNG olmalı ve fotoğrafla aynı boyutta (gerekirse ölçeklenir).</summary>
    Task<byte[]> NormalizeMaskAsync(Stream maskPng, int widthPx, int heightPx, CancellationToken cancellationToken);
}

/// <summary>Sahne katmanları + duvar geometrisi - render için gereken her şey.</summary>
public sealed record SceneRenderData(
    Guid Id,
    int Version,
    string BaseImageUrl,
    string? ShadowMapUrl,
    string? ForegroundMaskUrl,
    int ImageWidthPx,
    int ImageHeightPx,
    WallQuad WallQuad,
    decimal RealWallWidthCm,
    decimal RealWallHeightCm);

/// <summary>Posterin kaynağı: özel depodaki orijinal (varsa) veya herkese açık bir türev URL'si.</summary>
public sealed record PosterSource(string? PrivateKey, string? PublicUrl, WallProductType ProductType, decimal? RepeatWidthCm, decimal? RepeatHeightCm, RepeatType RepeatType);

public sealed record WallRenderRequest(
    SceneRenderData Scene,
    PosterSource Poster,
    WallConfiguration Configuration,
    WallAlign Align,
    decimal PanelWidthCm,
    decimal BleedCm,
    bool PanelLines,
    bool Watermark,
    int OutputWidthPx);

/// <summary>Sunucu tarafı duvar render'ı (spec 1.6.3/1.6.5). İstemcideki wall-visualizer.js ile AYNI
/// homografi ve katman sırası: taban → poster (perspektif) → gölge (multiply) → ön plan maskesi.
/// İleride AI tabanlı otomatik köşe/maske tespiti bu arayüzün arkasına eklenebilir.</summary>
public interface IWallPreviewRenderer
{
    Task<byte[]> RenderAsync(WallRenderRequest request, CancellationToken cancellationToken);
}

public sealed record ProductionRenderRequest(
    PosterSource Poster,
    WallConfiguration Configuration,
    decimal BleedCm,
    decimal PanelWidthCm,
    string OrderNumber,
    string ItemLabel,
    int TargetDpi = 150);

public sealed record ProductionPdf(byte[] Content, int PanelCount, int Dpi);

/// <summary>Üretim dosyası (spec 1.7): kırpma → esnet/kırp → ayna → filtre → hedef ölçü + pay, hedef 150 DPI,
/// panellere bölünmüş PDF, her panelde numara ve hizalama işareti. Onay önizlemesi: aynı üretim alanının
/// küçük, panel çizgili ve kesim sınırı işaretli görseli.</summary>
public interface IProductionFileRenderer
{
    Task<ProductionPdf> RenderPanelsPdfAsync(ProductionRenderRequest request, CancellationToken cancellationToken);
    Task<byte[]> RenderProofPreviewAsync(ProductionRenderRequest request, int widthPx, CancellationToken cancellationToken);
}

public sealed record GeneratedScene(
    string Name,
    RoomType RoomType,
    string BaseImageUrl,
    string? ShadowMapUrl,
    string? ForegroundMaskUrl,
    int WidthPx,
    int HeightPx,
    WallQuad WallQuad,
    decimal RealWallWidthCm,
    decimal RealWallHeightCm);

/// <summary>Gerçek oda fotoğrafları yüklenene kadar kullanılacak, prosedürel olarak üretilmiş hazır
/// sahneler (taban + gölge + ön plan maskesi katmanlarıyla). Admin bunları gerçek fotoğraflarla değiştirir.</summary>
public interface IDefaultSceneGenerator
{
    Task<IReadOnlyList<GeneratedScene>> GenerateAsync(CancellationToken cancellationToken);
}

/// <summary>Render sonuçlarının önbelleği (spec 1.6.5): dosya depolama + HybridCache, etiketle geçersiz kılma.</summary>
public interface IWallRenderCache
{
    Task<string> GetOrCreateAsync(string key, IReadOnlyCollection<string> tags, Func<CancellationToken, Task<byte[]>> factory, CancellationToken cancellationToken);
    Task InvalidateAsync(string tag, CancellationToken cancellationToken);
}
