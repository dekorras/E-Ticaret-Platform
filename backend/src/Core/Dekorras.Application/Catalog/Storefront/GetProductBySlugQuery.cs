using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Catalog.Storefront;

public sealed record StorefrontVariantDto(Guid Id, string OptionName, decimal? PriceAdjustmentTry);

public sealed record StorefrontAttributeDto(string Name, string Value);

public sealed record StorefrontVideoDto(string Url, string? Title);

public sealed record StorefrontProductDetailDto(
    Guid Id,
    string Slug,
    string ProductCode,
    string Name,
    string? Description,
    decimal PriceTry,
    decimal TaxRatePercentage,
    UnitOfMeasure UnitOfMeasure,
    int MinimumOrderQuantity,
    bool InStock,
    StockAvailability StockAvailability,
    string? BrandName,
    IReadOnlyCollection<string> ImageUrls,
    IReadOnlyCollection<StorefrontVariantDto> Variants,
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    decimal? AverageRating,
    int ReviewCount,
    IReadOnlyCollection<StorefrontAttributeDto> Attributes,
    IReadOnlyCollection<StorefrontVideoDto> Videos,
    string? MetaRobots,
    bool PricesVisible);

/// <summary>Ürün detay sayfası - yalnızca Aktif ürün döner; taslak/yayından kaldırılmış bir
/// ürünün slug'ına doğrudan gidilmeye çalışılsa bile mağazada 404 görünmelidir.</summary>
public sealed record GetProductBySlugQuery(string Slug, string LanguageCode, Guid? CustomerGroupId = null) : IRequest<StorefrontProductDetailDto?>;

public sealed class GetProductBySlugQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductBySlugQuery, StorefrontProductDetailDto?>
{
    public Task<StorefrontProductDetailDto?> Handle(GetProductBySlugQuery request, CancellationToken cancellationToken)
    {
        var approvedReviews = unitOfWork.Repository<ProductReview>().Query().Where(r => r.IsApproved);
        var allAttributes = unitOfWork.Repository<ProductAttribute>().Query();

        // Bkz. GetStorefrontProductsQuery'deki aynı not - misafirler HER ZAMAN fiyatı görür.
        var pricesVisible = request.CustomerGroupId is null || unitOfWork.Repository<CustomerGroup>().Query()
            .Where(g => g.Id == request.CustomerGroupId)
            .Select(g => g.ShowPricesOnStorefront)
            .FirstOrDefault();

        var dto = unitOfWork.Repository<Product>().Query()
            .Where(p => p.Slug == request.Slug && p.Status == ProductStatus.Active)
            .Select(p => new StorefrontProductDetailDto(
                p.Id,
                p.Slug,
                p.ProductCode,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Description).FirstOrDefault(),
                // Bkz. GetStorefrontProductsQuery'deki aynı not - müşteri grubuna özel fiyat varsa
                // (plan §2.6) o gösterilir.
                p.GroupPrices.Where(g => g.CustomerGroupId == request.CustomerGroupId).Select(g => (decimal?)g.PriceTry).FirstOrDefault() ?? p.BasePriceTry,
                p.TaxRatePercentage,
                p.UnitOfMeasure,
                p.MinimumOrderQuantity,
                p.StockAvailability == StockAvailability.InStock,
                p.StockAvailability,
                p.Brand != null ? p.Brand.Name : null,
                p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).ToList(),
                p.Variants.Select(v => new StorefrontVariantDto(v.Id, v.OptionName, v.PriceAdjustmentTry)).ToList(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaTitle).FirstOrDefault(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaDescription).FirstOrDefault(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaKeywords).FirstOrDefault(),
                approvedReviews.Where(r => r.ProductId == p.Id).Average(r => (decimal?)r.Rating),
                approvedReviews.Count(r => r.ProductId == p.Id),
                p.AttributeValues.Select(v => new StorefrontAttributeDto(
                    allAttributes.Where(a => a.Id == v.ProductAttributeId).Select(a => a.Name).FirstOrDefault() ?? "",
                    v.Value)).ToList(),
                p.Videos.Select(v => new StorefrontVideoDto(v.Url, v.Title)).ToList(),
                p.MetaRobots,
                pricesVisible))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}
