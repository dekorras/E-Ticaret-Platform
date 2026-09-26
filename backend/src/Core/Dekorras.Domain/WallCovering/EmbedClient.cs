using System.Security.Cryptography;
using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>"Duvarında Gör" widget'ını kullanan harici site (spec 1.6.6-D).</summary>
public class EmbedClient : AuditableEntity
{
    public string PublicKey { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    /// <summary>Satır/virgül ayrılmış tam origin listesi (ör. "https://magaza.com").</summary>
    public string AllowedOrigins { get; private set; } = "";

    /// <summary>Harici görselin indirilebileceği host listesi (ör. "cdn.shopify.com").</summary>
    public string AllowedImageHosts { get; private set; } = "";

    public int DailyQuota { get; private set; } = 1000;
    public bool IsActive { get; private set; } = true;
    public DateOnly? UsageDate { get; private set; }
    public int UsageCount { get; private set; }

    private EmbedClient() { }

    public EmbedClient(string name, IEnumerable<string> allowedOrigins, IEnumerable<string> allowedImageHosts, int dailyQuota)
    {
        PublicKey = "pk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        Update(name, allowedOrigins, allowedImageHosts, dailyQuota);
    }

    public void Update(string name, IEnumerable<string> allowedOrigins, IEnumerable<string> allowedImageHosts, int dailyQuota)
    {
        Name = name;
        AllowedOrigins = string.Join(",", NormalizeOrigins(allowedOrigins));
        AllowedImageHosts = string.Join(",", allowedImageHosts.Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0).Distinct());
        DailyQuota = Math.Max(0, dailyQuota);
    }

    public IReadOnlyList<string> Origins => Split(AllowedOrigins);
    public IReadOnlyList<string> ImageHosts => Split(AllowedImageHosts);

    public bool IsOriginAllowed(string? origin) =>
        origin is not null && Origins.Contains(origin.TrimEnd('/').ToLowerInvariant());

    public bool IsImageHostAllowed(string host) => ImageHosts.Contains(host.ToLowerInvariant());

    /// <summary>Günlük kotadan bir birim düşer; kota dolmuşsa false.</summary>
    public bool TryConsumeQuota(DateOnly today)
    {
        if (UsageDate != today)
        {
            UsageDate = today;
            UsageCount = 0;
        }
        if (UsageCount >= DailyQuota) return false;
        UsageCount++;
        return true;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private static IEnumerable<string> NormalizeOrigins(IEnumerable<string> origins) =>
        origins.Select(o => o.Trim().TrimEnd('/').ToLowerInvariant())
            .Where(o => o.StartsWith("https://", StringComparison.Ordinal) || o.StartsWith("http://", StringComparison.Ordinal))
            .Distinct();

    private static IReadOnlyList<string> Split(string value) =>
        value.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Harici siteden gelen, bu sistemde kayıtlı olmayan poster görseli (izinli alan adından indirilip türevleri üretilmiş).</summary>
public class ExternalImage : BaseEntity
{
    public Guid EmbedClientId { get; private set; }
    public string SourceUrl { get; private set; } = default!;
    public string SourceUrlHash { get; private set; } = default!;
    public string PreviewUrl { get; private set; } = default!;
    public string ThumbUrl { get; private set; } = default!;
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private ExternalImage() { }

    public ExternalImage(Guid embedClientId, string sourceUrl, string sourceUrlHash, string previewUrl, string thumbUrl, int widthPx, int heightPx)
    {
        EmbedClientId = embedClientId;
        SourceUrl = sourceUrl;
        SourceUrlHash = sourceUrlHash;
        PreviewUrl = previewUrl;
        ThumbUrl = thumbUrl;
        WidthPx = widthPx;
        HeightPx = heightPx;
    }
}

/// <summary>"Duvarında Gör" ölçüm olayı (spec 1.6.6-E).</summary>
public class WallPreviewEvent : BaseEntity
{
    public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>
    {
        "wall_preview_link_click", "wall_preview_scene_change", "wall_preview_product_swap", "wall_preview_add_to_cart"
    };

    public static readonly IReadOnlySet<string> KnownSources = new HashSet<string>
    {
        "kart", "detay", "sepet", "e-posta", "embed", "konfigurator", "goruntuleyici"
    };

    public string EventType { get; private set; } = default!;
    public string? Source { get; private set; }
    public Guid? ProductId { get; private set; }
    public string? VisitorKey { get; private set; }
    public DateTime OccurredAtUtc { get; private set; } = DateTime.UtcNow;

    private WallPreviewEvent() { }

    public WallPreviewEvent(string eventType, string? source, Guid? productId, string? visitorKey)
    {
        EventType = eventType;
        Source = source;
        ProductId = productId;
        VisitorKey = visitorKey;
    }
}
