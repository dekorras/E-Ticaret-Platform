using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Storefront;

public sealed record ComparisonProductDto(
    Guid Id,
    string Slug,
    string Name,
    decimal PriceTry,
    string? ImageUrl,
    bool InStock,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>Ürün karşılaştırma sayfası (bkz. plan §2.1 - "ürün listeleme sayfası: ... ürün
/// karşılaştırma listesi"). Yalnızca Aktif ürünler döner - karşılaştırma listesindeki bir ürün
/// yayından kaldırılmışsa sessizce sonuçtan düşer. `ProductAttribute`/`ProductAttributeValue`'dan
/// (devamı 29) satır bazlı özellik karşılaştırması üretir; her ürünün FARKLI özellikleri olabilir,
/// birleşik satır listesi View tarafında (tüm ürünlerin özellik adlarının BİRLEŞİMİ) kurulur.</summary>
public sealed record GetProductsForComparisonQuery(IReadOnlyCollection<Guid> ProductIds, string LanguageCode) : IRequest<IReadOnlyCollection<ComparisonProductDto>>;

public sealed class GetProductsForComparisonQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetProductsForComparisonQuery, IReadOnlyCollection<ComparisonProductDto>>
{
    public Task<IReadOnlyCollection<ComparisonProductDto>> Handle(GetProductsForComparisonQuery request, CancellationToken cancellationToken)
    {
        if (request.ProductIds.Count == 0)
            return Task.FromResult<IReadOnlyCollection<ComparisonProductDto>>([]);

        var allAttributes = unitOfWork.Repository<ProductAttribute>().Query();

        var products = unitOfWork.Repository<Product>().Query()
            .Where(p => request.ProductIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                Name = p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                p.BasePriceTry,
                ImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault(),
                InStock = p.StockAvailability == StockAvailability.InStock,
                AttributeValues = p.AttributeValues.Select(v => new { v.ProductAttributeId, v.Value }).ToList()
            })
            .ToList();

        var result = products
            .Select(p => new ComparisonProductDto(
                p.Id,
                p.Slug,
                p.Name,
                p.BasePriceTry,
                p.ImageUrl,
                p.InStock,
                p.AttributeValues.ToDictionary(
                    v => allAttributes.Where(a => a.Id == v.ProductAttributeId).Select(a => a.Name).FirstOrDefault() ?? "",
                    v => v.Value)))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ComparisonProductDto>>(result);
    }
}
