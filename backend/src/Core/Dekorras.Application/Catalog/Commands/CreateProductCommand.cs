using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record CreateProductCommand(
    string Slug,
    string ProductCode,
    decimal BasePriceTry,
    decimal TaxRatePercentage,
    UnitOfMeasure UnitOfMeasure,
    int MinimumOrderQuantity,
    int StockQuantity,
    Guid? BrandId,
    IReadOnlyCollection<Guid> CategoryIds,
    string LanguageCode,
    string Name,
    string? Description,
    string? MetaTitle = null,
    string? MetaDescription = null,
    string? MetaKeywords = null,
    decimal? WeightKg = null,
    string? HsCode = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    string? Sku = null,
    string? Upc = null,
    string? Ean = null,
    string? Jan = null,
    string? Isbn = null,
    string? Mpn = null,
    string? MetaRobots = null) : IRequest<Guid>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(250);
        RuleFor(x => x.ProductCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.BasePriceTry).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRatePercentage).InclusiveBetween(0, 100);
        RuleFor(x => x.MinimumOrderQuantity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.CategoryIds).NotEmpty().WithMessage("Ürün en az bir kategoriye atanmalıdır.");
        RuleFor(x => x.WeightKg).GreaterThan(0).When(x => x.WeightKg is not null);
        RuleFor(x => x.HsCode).MaximumLength(20);
        RuleFor(x => x.LengthCm).GreaterThan(0).When(x => x.LengthCm is not null);
        RuleFor(x => x.WidthCm).GreaterThan(0).When(x => x.WidthCm is not null);
        RuleFor(x => x.HeightCm).GreaterThan(0).When(x => x.HeightCm is not null);
        RuleFor(x => x.Sku).MaximumLength(64);
        RuleFor(x => x.Upc).MaximumLength(20);
        RuleFor(x => x.Ean).MaximumLength(20);
        RuleFor(x => x.Jan).MaximumLength(20);
        RuleFor(x => x.Isbn).MaximumLength(20);
        RuleFor(x => x.Mpn).MaximumLength(64);
        RuleFor(x => x.MetaRobots).MaximumLength(100);
    }
}

public sealed class CreateProductCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = new Product(request.Slug, request.ProductCode, request.BasePriceTry, request.TaxRatePercentage, request.UnitOfMeasure);
        product.SetMinimumOrderQuantity(request.MinimumOrderQuantity);
        product.UpdateStock(request.StockQuantity);
        product.AssignBrand(request.BrandId);
        product.SetWeight(request.WeightKg);
        product.SetHsCode(request.HsCode);
        product.SetPackageDimensions(request.LengthCm, request.WidthCm, request.HeightCm);
        product.SetIdentifiers(request.Sku, request.Upc, request.Ean, request.Jan, request.Isbn, request.Mpn);
        product.SetMetaRobots(request.MetaRobots);
        product.SetTranslation(request.LanguageCode, request.Name, request.Description, request.MetaTitle, request.MetaDescription, request.MetaKeywords);

        foreach (var categoryId in request.CategoryIds)
            product.AssignToCategory(categoryId, isPrimary: categoryId == request.CategoryIds.First());

        await unitOfWork.Repository<Product>().AddAsync(product, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Id;
    }
}
