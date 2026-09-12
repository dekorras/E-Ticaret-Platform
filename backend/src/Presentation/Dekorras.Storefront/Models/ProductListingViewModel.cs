using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;

namespace Dekorras.Storefront.Models;

/// <summary>Kategori ve Arama sayfalarının ortak modeli - ürün listesinin yanında filtre
/// formunun (fiyat aralığı, marka), sıralamanın ve sayfalamanın (bkz. plan §2.1) mevcut durumunu
/// da taşır.</summary>
public sealed record ProductListingViewModel(
    IReadOnlyCollection<StorefrontProductListItemDto> Products,
    IReadOnlyCollection<BrandDto> Brands,
    string? SearchText,
    decimal? MinPriceTry,
    decimal? MaxPriceTry,
    Guid? BrandId,
    ProductSortOrder SortBy,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
