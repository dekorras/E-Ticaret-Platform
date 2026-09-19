using Dekorras.Application.Catalog.Storefront;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductListItemDto(
    Guid Id,
    string Slug,
    string ProductCode,
    string Name,
    decimal PriceTry,
    int StockQuantity,
    ProductStatus Status,
    Guid? BrandId,
    string? BrandName,
    IReadOnlyCollection<Guid> CategoryIds);

/// <summary>
/// Admin panelindeki ürün listesi (bkz. backend/README.md - "Kategoriler sayfasındaki AYNI
/// kategorize etme/filtreleme mantığı"). Storefront'un `GetStorefrontProductsQuery`sinden FARKLI
/// olarak TÜM durumları (Taslak/Yayında Değil dahil) döner - admin HERŞEYİ görebilmeli. 1999+
/// ürünlü GERÇEK bir katalogda TÜMÜNÜ istemciye (DataTables'a) tek seferde göndermek Blazor
/// Server'ın render/SignalR yükünü ŞİŞİRECEĞİ için (bkz. CategoryList'in yalnızca 25 satırlık
/// AKSİNE), filtreleme BURADA - sunucu tarafında, SQL sorgusunun bir PARÇASI olarak - yapılır,
/// istemci tarafı `dekorrasDataTable.filterColumn` DEĞİL (o yalnızca ZATEN yüklenmiş, küçük bir
/// sonuç kümesi için uygundur).
/// </summary>
public sealed record GetProductsQuery(
    string LanguageCode,
    Guid? CategoryId,
    int Page = 1,
    int PageSize = 25,
    Guid? BrandId = null,
    ProductStatus? Status = null,
    bool? InStock = null)
    : IRequest<PagedResult<ProductListItemDto>>;

public sealed class GetProductsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductsQuery, PagedResult<ProductListItemDto>>
{
    public Task<PagedResult<ProductListItemDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<Product>().Query();

        if (request.CategoryId is not null)
        {
            // GetStorefrontProductsQuery'deki AYNI düzeltme (bkz. backend/README.md - "anasayfada
            // göster" kök neden zinciri): yalnızca TAM eşleşen kategoriyi değil, TÜM alt ağacını
            // da kapsar - aksi halde bir üst kategoriye göre filtrelemek, ürünleri yalnızca daha
            // alt bir alt kategoriye atanmışsa SESSİZCE gizlerdi.
            var categoryIds = ResolveCategoryAndDescendantIds(request.CategoryId.Value);
            query = query.Where(p => p.ProductCategories.Any(pc => categoryIds.Contains(pc.CategoryId)));
        }

        if (request.BrandId is not null)
            query = query.Where(p => p.BrandId == request.BrandId);

        if (request.Status is not null)
            query = query.Where(p => p.Status == request.Status);

        if (request.InStock is bool inStock)
            query = inStock ? query.Where(p => p.StockQuantity > 0) : query.Where(p => p.StockQuantity <= 0);

        var totalCount = query.Count();

        var items = query
            .OrderBy(p => p.DisplayOrder)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new ProductListItemDto(
                p.Id,
                p.Slug,
                p.ProductCode,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                p.BasePriceTry,
                p.StockQuantity,
                p.Status,
                p.BrandId,
                p.Brand == null ? null : p.Brand.Name,
                p.ProductCategories.Select(pc => pc.CategoryId).ToList()))
            .ToList()
            .AsReadOnly();

        return Task.FromResult(new PagedResult<ProductListItemDto>(items, totalCount, request.Page, request.PageSize));
    }

    /// <summary>GetStorefrontProductsQuery'deki AYNI yardımcı - bkz. oradaki açıklama.</summary>
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
