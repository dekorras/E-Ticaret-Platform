using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductDetailDto(
    Guid Id,
    string Slug,
    string ProductCode,
    decimal BasePriceTry,
    decimal TaxRatePercentage,
    UnitOfMeasure UnitOfMeasure,
    int MinimumOrderQuantity,
    int StockQuantity,
    StockAvailability StockAvailability,
    bool TrackStock,
    Guid? BrandId,
    IReadOnlyCollection<Guid> CategoryIds,
    string Name,
    string? Description,
    ProductStatus Status,
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    decimal? WeightKg,
    string? HsCode,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    string? Sku,
    string? Upc,
    string? Ean,
    string? Jan,
    string? Isbn,
    string? Mpn,
    string? MetaRobots);

public sealed record GetProductByIdQuery(Guid Id, string LanguageCode) : IRequest<ProductDetailDto?>;

public sealed class GetProductByIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductByIdQuery, ProductDetailDto?>
{
    public Task<ProductDetailDto?> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        // Not: GetByIdAsync (DbSet.FindAsync) gezinme özelliklerini eager-load ETMEZ; bu yüzden
        // Translations/ProductCategories'e hâlâ IQueryable iken, tek bir projeksiyon içinde erişiyoruz.
        var dto = unitOfWork.Repository<Product>().Query()
            .Where(p => p.Id == request.Id)
            .Select(p => new ProductDetailDto(
                p.Id,
                p.Slug,
                p.ProductCode,
                p.BasePriceTry,
                p.TaxRatePercentage,
                p.UnitOfMeasure,
                p.MinimumOrderQuantity,
                p.StockQuantity,
                p.StockAvailability,
                p.TrackStock,
                p.BrandId,
                p.ProductCategories.Select(pc => pc.CategoryId).ToList(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Description).FirstOrDefault(),
                p.Status,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaTitle).FirstOrDefault(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaDescription).FirstOrDefault(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaKeywords).FirstOrDefault(),
                p.Weight,
                p.HsCode,
                p.Length,
                p.Width,
                p.Height,
                p.Sku,
                p.Upc,
                p.Ean,
                p.Jan,
                p.Isbn,
                p.Mpn,
                p.MetaRobots))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}
