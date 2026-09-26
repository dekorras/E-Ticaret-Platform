using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>Bir katalog ürününü (Product) ölçüye özel duvar kağıdı olarak konfigüre edilebilir kılan
/// 1:1 profil. Product'ı şişirmemek ve mevcut (duvar kağıdı olmayan) ürünleri hiç etkilememek için
/// ayrı tablo. Fiyat buradan DEĞİL, malzeme ₺/m² tablosundan gelir.</summary>
public class WallpaperProfile : AuditableEntity
{
    public Guid ProductId { get; private set; }
    public WallProductType ProductType { get; private set; } = WallProductType.Mural;
    public decimal? RepeatWidthCm { get; private set; }
    public decimal? RepeatHeightCm { get; private set; }
    public RepeatType RepeatType { get; private set; } = RepeatType.Straight;

    /// <summary>Orijinal yüksek çözünürlüklü görselin ÖZEL depolamadaki anahtarı - asla herkese açık URL değildir.</summary>
    public string? OriginalImageKey { get; private set; }
    public int ImageWidthPx { get; private set; }
    public int ImageHeightPx { get; private set; }
    public int? Dpi { get; private set; }

    /// <summary>Genişlik / yükseklik. 1'den büyükse yatay, küçükse dikey (katalog "yönelim" filtresi).</summary>
    public decimal AspectRatio { get; private set; }

    /// <summary>Virgülle ayrılmış 3–5 HEX renk (ör. "#2f5d3a,#a3b18a").</summary>
    public string? DominantColors { get; private set; }

    public string? ThumbUrl { get; private set; }
    public string? ListUrl { get; private set; }
    public string? PreviewUrl { get; private set; }
    public string? LqipBase64 { get; private set; }
    public string? SceneThumbUrl { get; private set; }
    public DateTime? DerivativesGeneratedAtUtc { get; private set; }

    public int PopularityScore { get; private set; }
    public bool IsEnabled { get; private set; } = true;

    private WallpaperProfile() { }

    public WallpaperProfile(Guid productId, WallProductType productType = WallProductType.Mural)
    {
        ProductId = productId;
        ProductType = productType;
    }

    public void SetType(WallProductType productType, decimal? repeatWidthCm, decimal? repeatHeightCm, RepeatType repeatType)
    {
        if (productType == WallProductType.Pattern && (repeatWidthCm is not > 0 || repeatHeightCm is not > 0))
            throw new DomainException("Desen ürünlerde tekrar eni ve boyu girilmelidir.");

        ProductType = productType;
        RepeatWidthCm = productType == WallProductType.Pattern ? repeatWidthCm : null;
        RepeatHeightCm = productType == WallProductType.Pattern ? repeatHeightCm : null;
        RepeatType = repeatType;
    }

    public void SetOriginalImage(string originalImageKey, int widthPx, int heightPx, int? dpi)
    {
        if (widthPx <= 0 || heightPx <= 0) throw new DomainException("Görsel boyutu geçersiz.");
        OriginalImageKey = originalImageKey;
        ImageWidthPx = widthPx;
        ImageHeightPx = heightPx;
        Dpi = dpi;
        AspectRatio = Math.Round((decimal)widthPx / heightPx, 4, MidpointRounding.AwayFromZero);
    }

    public void SetDerivatives(string thumbUrl, string listUrl, string previewUrl, string lqipBase64, string? dominantColors)
    {
        ThumbUrl = thumbUrl;
        ListUrl = listUrl;
        PreviewUrl = previewUrl;
        LqipBase64 = lqipBase64;
        DominantColors = dominantColors;
        DerivativesGeneratedAtUtc = DateTime.UtcNow;
    }

    public void SetSceneThumb(string? sceneThumbUrl) => SceneThumbUrl = sceneThumbUrl;

    /// <summary>Türevleri ve sahne küçük resmini yeniden üretime kuyruğa alır. <paramref name="reloadOriginal"/>
    /// true ise orijinal de yeniden alınır (ürünün ana görseli değiştiyse).</summary>
    public void ResetDerivatives(bool reloadOriginal)
    {
        DerivativesGeneratedAtUtc = null;
        SceneThumbUrl = null;
        if (reloadOriginal) OriginalImageKey = null;
    }
    public void SetPopularity(int score) => PopularityScore = score;
    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}

public class Tag : BaseEntity
{
    public TagGroup Group { get; private set; }
    public string Value { get; private set; } = default!;
    public string Label { get; private set; } = default!;

    /// <summary>Yalnızca Color grubunda: filtre noktasının rengi (#RRGGBB).</summary>
    public string? Hex { get; private set; }

    private Tag() { }

    public Tag(TagGroup group, string value, string label, string? hex = null)
    {
        Group = group;
        Value = Normalize(value);
        Label = label;
        Hex = hex;
    }

    public void Update(string label, string? hex)
    {
        Label = label;
        Hex = hex;
    }

    /// <summary>"color:green" biçimi (URL ve CSV'de kullanılır).</summary>
    public string Key => $"{Group.ToString().ToLowerInvariant()}:{Value}";

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public class ProductTag : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid TagId { get; private set; }

    private ProductTag() { }

    public ProductTag(Guid productId, Guid tagId)
    {
        ProductId = productId;
        TagId = tagId;
    }
}
