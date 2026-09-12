using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Storefront;

/// <summary>Ürün detay sayfasındaki "Benzer Ürünler" bölümü (bkz. plan §2.4 - "Bağlantılar"
/// sekmesi: "ilgili ürünler"). Yalnızca Aktif (yayınlanmış) ilişkili ürünler döner - Admin'in
/// eklediği bir ürün sonradan yayından kaldırılırsa Storefront'ta sessizce düşer.</summary>
public sealed record GetActiveRelatedProductsQuery(Guid ProductId, string LanguageCode) : IRequest<IReadOnlyCollection<StorefrontProductListItemDto>>;

public sealed class GetActiveRelatedProductsQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetActiveRelatedProductsQuery, IReadOnlyCollection<StorefrontProductListItemDto>>
{
    public Task<IReadOnlyCollection<StorefrontProductListItemDto>> Handle(GetActiveRelatedProductsQuery request, CancellationToken cancellationToken)
    {
        var relatedIds = unitOfWork.Repository<Product>().Query()
            .Where(p => p.Id == request.ProductId)
            .SelectMany(p => p.RelatedProducts.Select(r => r.RelatedProductId));

        var result = unitOfWork.Repository<Product>().Query()
            .Where(p => relatedIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Select(p => new StorefrontProductListItemDto(
                p.Id,
                p.Slug,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                p.BasePriceTry,
                p.TaxRatePercentage,
                p.UnitOfMeasure,
                p.StockAvailability == StockAvailability.InStock,
                p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault(),
                // Bilinçli kapsam dışı (bkz. Müşteri Grubu Fiyatlandırması notu) - "Benzer Ürünler"
                // ikincil bir görünüm, grup fiyatı/görünürlüğü burada uygulanmaz.
                PricesVisible: true))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<StorefrontProductListItemDto>>(result);
    }
}
