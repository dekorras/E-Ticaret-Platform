using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record UpdateProductCommand(
    Guid Id,
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
    string? MetaRobots = null) : IRequest<Unit>;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(250);
        RuleFor(x => x.ProductCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.BasePriceTry).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRatePercentage).InclusiveBetween(0, 100);
        RuleFor(x => x.MinimumOrderQuantity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
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

public sealed class UpdateProductCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateProductCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı ürün bulunamadı.");

        // Translations/ProductCategories yüklenmeden SetTranslation/SetCategories çağrılırsa
        // mevcut kayıtlar bulunamaz sanılır ve yinelenen satırlar eklenir; kategori kaldırma da
        // sessizce çalışmaz - bkz. IRepository<T>.LoadCollectionAsync.
        await repository.LoadCollectionAsync(product, p => p.Translations, cancellationToken);
        await repository.LoadCollectionAsync(product, p => p.ProductCategories, cancellationToken);

        product.UpdateSlug(request.Slug);
        product.UpdateProductCode(request.ProductCode);
        product.UpdatePricing(request.BasePriceTry, request.TaxRatePercentage);
        product.UpdateUnitOfMeasure(request.UnitOfMeasure);
        product.SetMinimumOrderQuantity(request.MinimumOrderQuantity);
        product.UpdateStock(request.StockQuantity);
        product.AssignBrand(request.BrandId);
        product.SetWeight(request.WeightKg);
        product.SetHsCode(request.HsCode);
        product.SetPackageDimensions(request.LengthCm, request.WidthCm, request.HeightCm);
        product.SetIdentifiers(request.Sku, request.Upc, request.Ean, request.Jan, request.Isbn, request.Mpn);
        product.SetMetaRobots(request.MetaRobots);
        product.SetCategories(request.CategoryIds);
        // Update(product) BİLİNÇLİ OLARAK çağrılmaz: SetCategories/SetTranslation yeni çocuk
        // entity'ler ekleyebilir (yeni bir kategori ataması, yeni bir dil çevirisi) - Update()
        // çağrılsaydı bunları "Modified" sanıp DbUpdateConcurrencyException fırlatırdı -
        // bkz. TransitionOrderStatusCommand'daki not.
        product.SetTranslation(request.LanguageCode, request.Name, request.Description, request.MetaTitle, request.MetaDescription, request.MetaKeywords);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
