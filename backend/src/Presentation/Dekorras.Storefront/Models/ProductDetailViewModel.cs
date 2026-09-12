using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;

namespace Dekorras.Storefront.Models;

/// <summary>Ürün detay sayfasının modeli - ürün bilgisiyle birlikte onaylı değerlendirmeleri,
/// yanıtlanmış soruları ve (varsa) az önce yapılan gönderim denemesinin hata mesajını taşır.</summary>
public sealed record ProductDetailViewModel(
    StorefrontProductDetailDto Product,
    IReadOnlyCollection<ProductReviewDto> Reviews,
    IReadOnlyCollection<ProductQuestionDto> Questions,
    string? ReviewError,
    string? QuestionError,
    bool IsInWishlist = false,
    IReadOnlyCollection<StorefrontProductListItemDto>? RelatedProducts = null);
