using System.Globalization;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

public sealed record WallCardDto(
    Guid ProductId,
    string Slug,
    string Title,
    string? ThumbUrl,
    string? ListUrl,
    string? LqipBase64,
    string? SceneThumbUrl,
    IReadOnlyList<string> Colors,
    decimal AspectRatio,
    string ProductType,
    decimal FromPricePerM2);

public sealed record WallTagDto(string Key, string Label, string? Hex, int Count, bool Selected);

public sealed record WallTagGroupDto(string Group, string Label, IReadOnlyList<WallTagDto> Tags);

public sealed record WallCatalogResult(
    IReadOnlyList<WallCardDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<WallTagGroupDto> TagGroups);

/// <param name="Tags">"grup:değer" anahtarları. Aynı grup içinde VEYA, gruplar arasında VE.</param>
/// <param name="Orientation">"yatay" | "dikey" | "kare".</param>
/// <param name="Color">"#RRGGBB" - baskın renklere yakınlığa göre filtreler ve sıralar.</param>
/// <param name="Sort">"cok-satan" (varsayılan) | "yeni" | "fiyat-artan" | "fiyat-azalan".</param>
public sealed record GetWallCatalogQuery(
    IReadOnlyList<string>? Tags = null,
    string? Type = null,
    string? Orientation = null,
    string? Color = null,
    string? Search = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 24,
    IReadOnlyCollection<Guid>? OnlyProductIds = null,
    string? Category = null) : IRequest<WallCatalogResult>;

public sealed class GetWallCatalogQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallCatalogQuery, WallCatalogResult>
{
    // VARSAYIM: RGB uzayında öklid mesafesi 80'in altı "yakın renk" sayılır (0–441 aralığı).
    private const double ColorDistanceThreshold = 80d;

    public Task<WallCatalogResult> Handle(GetWallCatalogQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 60);
        var page = Math.Max(1, request.Page);

        var products = unitOfWork.Repository<Product>().Query().Where(p => p.Status == ProductStatus.Active);
        var profiles = unitOfWork.Repository<WallpaperProfile>().Query().Where(w => w.IsEnabled);
        var productTags = unitOfWork.Repository<ProductTag>().Query();

        var query = profiles.Join(products, w => w.ProductId, p => p.Id, (w, p) => new { w, p });

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            // Kategori + tüm alt kategorileri (ör. "Posterler" → "Duvar Posterleri").
            var categoryIds = EnsureWallpaperProfilesCommandHandler.ResolveCategoryTree(unitOfWork, [request.Category.Trim()]);
            var inCategory = unitOfWork.Repository<ProductCategory>().Query().Where(pc => categoryIds.Contains(pc.CategoryId));
            query = query.Where(x => inCategory.Any(pc => pc.ProductId == x.p.Id));
        }

        if (request.OnlyProductIds is { } ids)
            query = query.Where(x => ids.Contains(x.p.Id));

        if (Enum.TryParse<WallProductType>(request.Type, ignoreCase: true, out var type))
            query = query.Where(x => x.w.ProductType == type);

        query = request.Orientation?.ToLowerInvariant() switch
        {
            // AspectRatio 0 = görsel henüz işlenmedi → yönelim filtresinde yer almaz.
            "yatay" => query.Where(x => x.w.AspectRatio > 1.05m),
            "dikey" => query.Where(x => x.w.AspectRatio > 0 && x.w.AspectRatio < 0.95m),
            "kare" => query.Where(x => x.w.AspectRatio >= 0.95m && x.w.AspectRatio <= 1.05m),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.p.Translations.Any(t => t.LanguageCode == "tr" && t.Name.Contains(term)) || x.p.ProductCode.Contains(term));
        }

        var selectedTags = ParseTagKeys(request.Tags);
        var allTags = unitOfWork.Repository<Tag>().Query().ToList();
        foreach (var group in selectedTags.GroupBy(t => t.Group))
        {
            var tagIds = allTags.Where(t => t.Group == group.Key && group.Any(s => s.Value == t.Value)).Select(t => t.Id).ToList();
            query = query.Where(x => productTags.Any(pt => pt.ProductId == x.p.Id && tagIds.Contains(pt.TagId)));
        }

        var minMaterialPrice = unitOfWork.Repository<Material>().Query().Where(m => m.IsActive).Select(m => (decimal?)m.PricePerM2).Min() ?? 0m;
        var overrides = unitOfWork.Repository<ProductMaterialOverride>().Query();
        var orderItems = unitOfWork.Repository<OrderItem>().Query();

        var projected = query.Select(x => new
        {
            x.p.Id,
            x.p.Slug,
            x.p.ProductCode,
            x.p.CreatedAtUtc,
            Title = x.p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault(),
            FallbackImage = x.p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
            x.w.ThumbUrl,
            x.w.ListUrl,
            x.w.LqipBase64,
            x.w.SceneThumbUrl,
            x.w.DominantColors,
            x.w.AspectRatio,
            x.w.ProductType,
            x.w.PopularityScore,
            MinOverride = overrides.Where(o => o.ProductId == x.p.Id && o.IsAllowed && o.PricePerM2 != null).Min(o => o.PricePerM2),
            Sold = orderItems.Where(i => i.ProductId == x.p.Id).Sum(i => (int?)i.Quantity) ?? 0,
        });

        projected = request.Sort switch
        {
            "yeni" => projected.OrderByDescending(x => x.CreatedAtUtc),
            "fiyat-artan" => projected.OrderBy(x => x.MinOverride ?? minMaterialPrice).ThenByDescending(x => x.Sold),
            "fiyat-azalan" => projected.OrderByDescending(x => x.MinOverride ?? minMaterialPrice).ThenByDescending(x => x.Sold),
            _ => projected.OrderByDescending(x => x.Sold).ThenByDescending(x => x.PopularityScore).ThenByDescending(x => x.CreatedAtUtc)
        };

        List<Guid> orderedIds;
        int total;
        var targetColor = TryParseHex(request.Color);
        if (targetColor is not null)
        {
            // Renk yakınlığı SQL'e çevrilemez: yalnızca kimlik+renkler çekilip bellekte süzülür (~2 bin ürün).
            var candidates = projected.Select(x => new { x.Id, x.DominantColors }).ToList()
                .Select(x => new { x.Id, Distance = MinDistance(targetColor.Value, x.DominantColors) })
                .Where(x => x.Distance < ColorDistanceThreshold)
                .OrderBy(x => x.Distance)
                .ToList();
            total = candidates.Count;
            orderedIds = candidates.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToList();
        }
        else
        {
            total = projected.Count();
            orderedIds = projected.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToList();
        }

        var rows = projected.Where(x => orderedIds.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
        var items = orderedIds.Where(rows.ContainsKey).Select(id =>
        {
            var r = rows[id];
            return new WallCardDto(r.Id, r.Slug, r.Title ?? r.ProductCode, r.ThumbUrl ?? r.FallbackImage, r.ListUrl ?? r.FallbackImage,
                r.LqipBase64, r.SceneThumbUrl, SplitColors(r.DominantColors), r.AspectRatio, r.ProductType.ToString(), r.MinOverride ?? minMaterialPrice);
        }).ToList();

        return Task.FromResult(new WallCatalogResult(items, total, page, pageSize, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)),
            BuildTagGroups(allTags, selectedTags)));
    }

    private List<WallTagGroupDto> BuildTagGroups(List<Tag> allTags, IReadOnlyList<(TagGroup Group, string Value)> selected)
    {
        var counts = unitOfWork.Repository<ProductTag>().Query().GroupBy(pt => pt.TagId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);

        return allTags
            .Where(t => counts.ContainsKey(t.Id))
            .GroupBy(t => t.Group)
            .OrderBy(g => g.Key)
            .Select(g => new WallTagGroupDto(
                g.Key.ToString().ToLowerInvariant(),
                GroupLabel(g.Key),
                g.OrderByDescending(t => counts[t.Id]).Select(t => new WallTagDto(t.Key, t.Label, t.Hex, counts[t.Id], selected.Contains((t.Group, t.Value)))).ToList()))
            .ToList();
    }

    public static string GroupLabel(TagGroup group) => group switch
    {
        TagGroup.Color => "Renk",
        TagGroup.Room => "Oda",
        TagGroup.Style => "Stil",
        TagGroup.Theme => "Tema",
        TagGroup.Nature => "Doğa",
        TagGroup.Tone => "Ton",
        TagGroup.Density => "Yoğunluk",
        TagGroup.Scale => "Ölçek",
        TagGroup.Light => "Işık",
        _ => group.ToString()
    };

    public static IReadOnlyList<(TagGroup Group, string Value)> ParseTagKeys(IEnumerable<string>? keys) =>
        (keys ?? [])
            .SelectMany(k => k.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(k => k.Split(':', 2))
            .Where(p => p.Length == 2 && Enum.TryParse<TagGroup>(p[0], ignoreCase: true, out _))
            .Select(p => (Enum.Parse<TagGroup>(p[0], ignoreCase: true), Tag.Normalize(p[1])))
            .Distinct()
            .ToList();

    public static IReadOnlyList<string> SplitColors(string? colors) =>
        string.IsNullOrWhiteSpace(colors) ? [] : colors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static (int R, int G, int B)? TryParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public static double MinDistance((int R, int G, int B) target, string? colors) =>
        SplitColors(colors)
            .Select(TryParseHex)
            .Where(c => c is not null)
            .Select(c => Math.Sqrt(Math.Pow(c!.Value.R - target.R, 2) + Math.Pow(c.Value.G - target.G, 2) + Math.Pow(c.Value.B - target.B, 2)))
            .DefaultIfEmpty(double.MaxValue)
            .Min();
}

public sealed record WallProductDetailDto(
    Guid ProductId,
    string Slug,
    string Title,
    string? Description,
    string? PreviewUrl,
    string? ThumbUrl,
    string? LqipBase64,
    int ImageWidthPx,
    int ImageHeightPx,
    decimal AspectRatio,
    string ProductType,
    decimal? RepeatWidthCm,
    decimal? RepeatHeightCm,
    string RepeatType,
    IReadOnlyList<string> Colors,
    IReadOnlyList<WallTagDto> Tags,
    decimal FromPricePerM2,
    decimal TaxRatePercentage,
    string? MetaTitle,
    string? MetaDescription);

/// <summary>Görüntüleyici ve konfigüratör için ürün detayı (spec 1.9 - GET /products/{slug}). Orijinal
/// yüksek çözünürlüklü görsel ASLA dönmez; yalnızca filigranlı önizleme türevi (yoksa ürün görseli).</summary>
public sealed record GetWallProductDetailQuery(string Slug) : IRequest<WallProductDetailDto?>;

public sealed class GetWallProductDetailQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallProductDetailQuery, WallProductDetailDto?>
{
    public Task<WallProductDetailDto?> Handle(GetWallProductDetailQuery request, CancellationToken cancellationToken)
    {
        // İki ayrı basit sorgu: profildeki büyük metin sütunları (LQIP base64, renkler) çeviri/görsel sıralamalı
        // sorguya KATILMAZ. Tek sorguda SQL Server sıralama için ~44 MB bellek izni istiyor, bellek darken (SQL Express)
        // RESOURCE_SEMAPHORE'da ~25 sn bekliyordu - ürün sayfası ve "Duvarında Gör" bu yüzden yavaşlıyordu.
        var row = unitOfWork.Repository<Product>().Query()
            .Where(p => p.Slug == request.Slug && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.ProductCode,
                p.TaxRatePercentage,
                Translation = p.Translations.Where(t => t.LanguageCode == "tr").Select(t => new { t.Name, t.Description, t.MetaTitle, t.MetaDescription }).FirstOrDefault(),
                FallbackImage = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault()
            })
            .FirstOrDefault();
        var profile = row is null ? null : unitOfWork.Repository<WallpaperProfile>().Query().FirstOrDefault(w => w.ProductId == row.Id && w.IsEnabled);

        if (row is null || profile is null) return Task.FromResult<WallProductDetailDto?>(null);

        var tags = unitOfWork.Repository<ProductTag>().Query().Where(pt => pt.ProductId == row.Id)
            .Join(unitOfWork.Repository<Tag>().Query(), pt => pt.TagId, t => t.Id, (pt, t) => t)
            .ToList()
            .Select(t => new WallTagDto(t.Key, t.Label, t.Hex, 0, false))
            .ToList();

        var minMaterial = unitOfWork.Repository<Material>().Query().Where(m => m.IsActive).Select(m => (decimal?)m.PricePerM2).Min() ?? 0m;
        var minOverride = unitOfWork.Repository<ProductMaterialOverride>().Query()
            .Where(o => o.ProductId == row.Id && o.IsAllowed && o.PricePerM2 != null).Select(o => o.PricePerM2).Min();

        var w = profile;
        return Task.FromResult<WallProductDetailDto?>(new WallProductDetailDto(
            row.Id, row.Slug, row.Translation?.Name ?? row.ProductCode, row.Translation?.Description,
            w.PreviewUrl ?? row.FallbackImage, w.ThumbUrl ?? row.FallbackImage, w.LqipBase64,
            w.ImageWidthPx, w.ImageHeightPx, w.AspectRatio, w.ProductType.ToString(), w.RepeatWidthCm, w.RepeatHeightCm, w.RepeatType.ToString(),
            GetWallCatalogQueryHandler.SplitColors(w.DominantColors), tags, minOverride ?? minMaterial, row.TaxRatePercentage,
            row.Translation?.MetaTitle, row.Translation?.MetaDescription));
    }
}

/// <summary>Genel ürün kartları (anasayfa blokları, kategori sayfaları, benzer ürünler) için: verilen ürünlerden
/// hangileri ölçüye özel konfigüre edilebilir ve "m²'den başlayan" fiyatları. Tek sorguda toplu çalışır.
/// Başlangıç fiyatı katalogla AYNI kurala göre hesaplanır (ürün istisnası varsa o, yoksa en ucuz aktif malzeme).</summary>
public sealed record GetWallCardInfoQuery(IReadOnlyCollection<Guid> ProductIds) : IRequest<IReadOnlyDictionary<Guid, decimal>>;

public sealed class GetWallCardInfoQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallCardInfoQuery, IReadOnlyDictionary<Guid, decimal>>
{
    public Task<IReadOnlyDictionary<Guid, decimal>> Handle(GetWallCardInfoQuery request, CancellationToken cancellationToken)
    {
        if (request.ProductIds.Count == 0) return Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());

        var ids = request.ProductIds.Distinct().ToList();
        var configurable = unitOfWork.Repository<WallpaperProfile>().Query()
            .Where(p => ids.Contains(p.ProductId) && p.IsEnabled).Select(p => p.ProductId).ToList();
        if (configurable.Count == 0) return Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());

        var minMaterialPrice = unitOfWork.Repository<Material>().Query().Where(m => m.IsActive).Select(m => (decimal?)m.PricePerM2).Min() ?? 0m;
        var overrides = unitOfWork.Repository<ProductMaterialOverride>().Query()
            .Where(o => configurable.Contains(o.ProductId) && o.IsAllowed && o.PricePerM2 != null)
            .GroupBy(o => o.ProductId).Select(g => new { g.Key, Min = g.Min(o => o.PricePerM2) }).ToList()
            .ToDictionary(x => x.Key, x => x.Min!.Value);

        IReadOnlyDictionary<Guid, decimal> result = configurable.ToDictionary(id => id, id => overrides.GetValueOrDefault(id, minMaterialPrice));
        return Task.FromResult(result);
    }
}

public sealed record WallCategoryDto(string Slug, string Name, int Depth, int ProductCount);

public sealed record WallCategoriesResult(IReadOnlyList<WallCategoryDto> Categories, string? ProductCategorySlug);

/// <summary>Görüntüleyici/katalogdaki kategori seçimi: ayarlardaki duvar kategorileri (Duvar Kağıtları, Posterler)
/// ve alt kategorileri, ağaç sırasıyla (Depth = girinti). Sayılar alt kategoriler DAHİL konfigüre edilebilir
/// ürün sayısıdır; boş kategoriler listelenmez. ProductId verilirse ürünün ana (birincil) kategorisinin bağlı olduğu
/// kök kategori döner - görüntüleyici listesi varsayılan olarak o kategoriyle açılır.</summary>
public sealed record GetWallCategoriesQuery(Guid? ProductId = null) : IRequest<WallCategoriesResult>;

public sealed class GetWallCategoriesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallCategoriesQuery, WallCategoriesResult>
{
    public Task<WallCategoriesResult> Handle(GetWallCategoriesQuery request, CancellationToken cancellationToken)
    {
        var settings = WallCoveringSettings.Load(unitOfWork);
        var all = unitOfWork.Repository<Category>().Query().Where(c => c.IsActive)
            .Select(c => new
            {
                c.Id, c.Slug, c.ParentCategoryId, c.DisplayOrder,
                Name = c.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault()
            })
            .ToList();
        var children = all.Where(c => c.ParentCategoryId is not null).ToLookup(c => c.ParentCategoryId!.Value);
        var roots = settings.CategorySlugs.Select(s => all.FirstOrDefault(c => c.Slug == s)).Where(c => c is not null).ToList();
        var treeIds = EnsureWallpaperProfilesCommandHandler.ResolveCategoryTree(unitOfWork, settings.CategorySlugs);

        var configurable = unitOfWork.Repository<WallpaperProfile>().Query().Where(w => w.IsEnabled).Select(w => w.ProductId);
        var active = unitOfWork.Repository<Product>().Query().Where(p => p.Status == ProductStatus.Active).Select(p => p.Id);
        var links = unitOfWork.Repository<ProductCategory>().Query()
            .Where(pc => treeIds.Contains(pc.CategoryId) && configurable.Contains(pc.ProductId) && active.Contains(pc.ProductId))
            .Select(pc => new { pc.CategoryId, pc.ProductId })
            .ToList()
            .ToLookup(x => x.CategoryId, x => x.ProductId);

        var result = new List<WallCategoryDto>();
        HashSet<Guid> Collect(Guid id)
        {
            var set = links[id].ToHashSet();
            foreach (var child in children[id]) set.UnionWith(Collect(child.Id));
            return set;
        }
        void Walk(Guid id, string slug, string? name, int depth)
        {
            var count = Collect(id).Count;
            if (count == 0) return;
            result.Add(new WallCategoryDto(slug, name ?? slug, depth, count));
            foreach (var child in children[id].OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name))
                Walk(child.Id, child.Slug, child.Name, depth + 1);
        }
        foreach (var root in roots) Walk(root!.Id, root.Slug, root.Name, 0);

        string? productRoot = null;
        if (request.ProductId is Guid productId)
        {
            var productCategories = unitOfWork.Repository<ProductCategory>().Query().Where(pc => pc.ProductId == productId)
                .OrderByDescending(pc => pc.IsPrimary).Select(pc => pc.CategoryId).ToList();
            var parents = all.ToDictionary(c => c.Id, c => c.ParentCategoryId);
            foreach (var categoryId in productCategories)
            {
                // Kategoriden köke doğru çıkılır; ayarlardaki köklerden birine varılırsa o seçilir.
                Guid? current = categoryId;
                for (var guard = 0; current is Guid cur && guard < 20; guard++)
                {
                    var hit = roots.FirstOrDefault(r => r!.Id == cur);
                    if (hit is not null) { productRoot = hit.Slug; break; }
                    current = parents.GetValueOrDefault(cur);
                }
                if (productRoot is not null) break;
            }
        }

        return Task.FromResult(new WallCategoriesResult(result, productRoot));
    }
}
