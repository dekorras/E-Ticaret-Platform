using Dekorras.Application.WallCovering;

namespace Dekorras.Storefront.Models;

/// <summary>Katalog URL parametreleri: ?tag=color:green&amp;tag=room:salon&amp;type=mural&amp;orientation=yatay&amp;color=%23a3b18a&amp;q=&amp;sort=yeni&amp;page=2</summary>
public sealed class WallCatalogRequest
{
    public List<string> Tag { get; set; } = [];
    public string? Category { get; set; }
    public string? Type { get; set; }
    public string? Orientation { get; set; }
    public string? Color { get; set; }
    public string? Q { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;

    public const int PageSize = 24;

    public GetWallCatalogQuery ToQuery() => new(Tag, Type, Orientation, Color, Q, Sort, Math.Max(1, Page), PageSize, Category: Category);

    /// <summary>Filtre durumunu koruyarak sayfa bağlantısı (SEO için gerçek ?page= bağlantıları).</summary>
    public string QueryString(int? page = null)
    {
        var parts = new List<string>();
        parts.AddRange(Tag.Select(t => $"tag={Uri.EscapeDataString(t)}"));
        void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{key}={Uri.EscapeDataString(value)}"); }
        Add("category", Category);
        Add("type", Type);
        Add("orientation", Orientation);
        Add("color", Color);
        Add("q", Q);
        Add("sort", Sort);
        if ((page ?? Page) > 1) parts.Add($"page={page ?? Page}");
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }
}

public sealed record WallCatalogPageModel(WallCatalogRequest Request, WallCatalogResult Result, decimal FromPricePerM2)
{
    /// <summary>Kategori filtresi seçenekleri (Duvar Kağıtları / Posterler + alt kategoriler).</summary>
    public IReadOnlyList<WallCategoryDto> Categories { get; init; } = [];
}
