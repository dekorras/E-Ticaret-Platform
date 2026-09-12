using Dekorras.Application.Catalog.Storefront;

namespace Dekorras.Storefront.Models;

/// <summary>
/// Anasayfada, öne-çıkan bir kategori için render edilen tek bir "blok" - bkz.
/// GetFeaturedHomepageCategoriesQuery ve HomeController.Index. Anasayfanın artık tüm ürünleri
/// filtresiz göstermek yerine modüler kategori bloklarından oluşmasını sağlar.
/// </summary>
public sealed record HomeCategoryBlock(string CategoryName, string CategorySlug, IReadOnlyCollection<StorefrontProductListItemDto> Products);
