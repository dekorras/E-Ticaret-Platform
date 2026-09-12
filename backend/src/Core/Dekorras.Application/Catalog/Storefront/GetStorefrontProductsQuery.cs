using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Catalog.Storefront;

public sealed record StorefrontProductListItemDto(
    Guid Id,
    string Slug,
    string Name,
    decimal PriceTry,
    decimal TaxRatePercentage,
    UnitOfMeasure UnitOfMeasure,
    bool InStock,
    string? ImageUrl,
    bool PricesVisible);

/// <summary>Bkz. plan §2.1 - "ürün listeleme sayfası: sıralama (varsayılan, ad A-Z/Z-A, ucuzdan-
/// pahalıya, pahalıdan-ucuza, puana göre, ürün koduna göre)".</summary>
public enum ProductSortOrder
{
    Default,
    NameAsc,
    NameDesc,
    PriceAsc,
    PriceDesc,
    Rating,
    ProductCode
}

/// <summary>Sayfalama meta verisiyle birlikte bir sonuç sayfası (bkz. plan §2.1 - "sayfa başına
/// gösterim (12/25/50/75/100)").</summary>
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// Mağazadaki ürün listeleme/kategori/arama sayfası - yalnızca Aktif (yayınlanmış) ürünler döner;
/// Taslak veya Yayından Kaldırılmış ürünler asla müşteriye görünmez. <paramref name="SearchText"/>
/// ürün adı/açıklamasında arar (yalnızca istenen dildeki çeviride); <paramref name="MinPriceTry"/>/
/// <paramref name="MaxPriceTry"/>/<paramref name="BrandId"/> isteğe bağlı filtrelerdir.
/// </summary>
public sealed record GetStorefrontProductsQuery(
    string LanguageCode,
    Guid? CategoryId,
    int Page = 1,
    int PageSize = 24,
    string? SearchText = null,
    decimal? MinPriceTry = null,
    decimal? MaxPriceTry = null,
    Guid? BrandId = null,
    ProductSortOrder SortBy = ProductSortOrder.Default,
    Guid? CustomerGroupId = null)
    : IRequest<PagedResult<StorefrontProductListItemDto>>;

public sealed class GetStorefrontProductsQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetStorefrontProductsQuery, PagedResult<StorefrontProductListItemDto>>
{
    public Task<PagedResult<StorefrontProductListItemDto>> Handle(GetStorefrontProductsQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<Product>().Query().Where(p => p.Status == ProductStatus.Active);

        if (request.CategoryId is not null)
        {
            // Yalnızca TAM eşleşen kategoriyi değil, onun TÜM alt ağacını da kapsar - aksi halde
            // bir üst/kök kategoriye göre filtrelendiğinde (ör. anasayfa blokları, ürün vitrini,
            // kategori gezinme sayfası), ürünler yalnızca DAHA ALT bir alt kategoriye atanmışsa
            // (kök kategorinin kendisine DOĞRUDAN atanmamışsa) SESSİZCE hiç sonuç dönmüyordu - bu,
            // "anasayfada göster" bayrağının "hiçbir şey yapmıyormuş" gibi görünmesine yol açan
            // GERÇEK kök nedendi. Bkz. backend/README.md.
            var categoryIds = ResolveCategoryAndDescendantIds(request.CategoryId.Value);
            query = query.Where(p => p.ProductCategories.Any(pc => categoryIds.Contains(pc.CategoryId)));
        }

        if (request.BrandId is not null)
            query = query.Where(p => p.BrandId == request.BrandId);

        if (request.MinPriceTry is decimal minPrice)
            query = query.Where(p => p.BasePriceTry >= minPrice);

        if (request.MaxPriceTry is decimal maxPrice)
            query = query.Where(p => p.BasePriceTry <= maxPrice);

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.Trim();
            query = query.Where(p => p.Translations.Any(t =>
                t.LanguageCode == request.LanguageCode &&
                (t.Name.Contains(search) || (t.Description != null && t.Description.Contains(search)))));
        }

        var totalCount = query.Count();

        // Misafirler (grup yok) HER ZAMAN fiyatı görür - bu bayrak yalnızca BELİRLİ bir gruba
        // üye olup admin tarafından bilinçli olarak fiyatı gizlenmiş müşterileri etkiler (plan
        // §2.6 - "fiyatları sadece belirli gruplara gösterme"). Tüm sayfa için TEK bir değer
        // olduğundan (istekteki CustomerGroupId sabit), satır başına sorgu yerine bir kez hesaplanır.
        var pricesVisible = request.CustomerGroupId is null || unitOfWork.Repository<CustomerGroup>().Query()
            .Where(g => g.Id == request.CustomerGroupId)
            .Select(g => g.ShowPricesOnStorefront)
            .FirstOrDefault();

        var approvedReviews = unitOfWork.Repository<ProductReview>().Query().Where(r => r.IsApproved);

        query = request.SortBy switch
        {
            ProductSortOrder.NameAsc => query.OrderBy(p => p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault()),
            ProductSortOrder.NameDesc => query.OrderByDescending(p => p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault()),
            ProductSortOrder.PriceAsc => query.OrderBy(p => p.BasePriceTry),
            ProductSortOrder.PriceDesc => query.OrderByDescending(p => p.BasePriceTry),
            ProductSortOrder.Rating => query.OrderByDescending(p => approvedReviews.Where(r => r.ProductId == p.Id).Average(r => (decimal?)r.Rating) ?? 0m),
            ProductSortOrder.ProductCode => query.OrderBy(p => p.ProductCode),
            _ => query.OrderBy(p => p.DisplayOrder)
        };

        var items = query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new StorefrontProductListItemDto(
                p.Id,
                p.Slug,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                // Müşteri grubuna özel bir fiyat varsa (bkz. ProductGroupPrice, plan §2.6) o
                // gösterilir - fiyat ARALIĞI filtresi/sıralaması BİLİNÇLİ OLARAK hâlâ taban fiyata
                // göre çalışır (nadir kullanılan bir B2B kişiselleştirmesi için liste-genelinde bir
                // sıralama/filtre karmaşıklığına girmeye değmez).
                p.GroupPrices.Where(g => g.CustomerGroupId == request.CustomerGroupId).Select(g => (decimal?)g.PriceTry).FirstOrDefault() ?? p.BasePriceTry,
                p.TaxRatePercentage,
                p.UnitOfMeasure,
                p.StockAvailability == StockAvailability.InStock,
                p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault(),
                pricesVisible))
            .ToList()
            .AsReadOnly();

        return Task.FromResult(new PagedResult<StorefrontProductListItemDto>(items, totalCount, request.Page, request.PageSize));
    }

    /// <summary>
    /// Verilen kategorinin KENDİSİ + TÜM alt ağacındaki (sınırsız derinlik) kategori id'lerini
    /// döner - GetCategoryTreeQuery'nin düz-çek-bellekte-gez desenindeki AYNI fikir, ama burada
    /// yalnızca Id/ParentCategoryId çiftleri gerektiği için çok daha hafif bir izdüşüm kullanılır.
    /// </summary>
    private HashSet<Guid> ResolveCategoryAndDescendantIds(Guid rootCategoryId)
    {
        var childIdsByParent = unitOfWork.Repository<Category>().Query()
            .Where(c => c.ParentCategoryId != null)
            .Select(c => new { c.Id, ParentCategoryId = c.ParentCategoryId!.Value })
            .ToList()
            .GroupBy(c => c.ParentCategoryId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        var result = new HashSet<Guid> { rootCategoryId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootCategoryId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!childIdsByParent.TryGetValue(currentId, out var childIds)) continue;

            foreach (var childId in childIds)
            {
                if (result.Add(childId)) queue.Enqueue(childId);
            }
        }

        return result;
    }
}
