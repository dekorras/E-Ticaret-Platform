using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record AddProductVariantCommand(Guid ProductId, string Sku, string OptionName, decimal? PriceAdjustmentTry, int StockQuantity) : IRequest<Guid>;

public sealed class AddProductVariantCommandValidator : AbstractValidator<AddProductVariantCommand>
{
    public AddProductVariantCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OptionName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
    }
}

public sealed class AddProductVariantCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddProductVariantCommand, Guid>
{
    public async Task<Guid> Handle(AddProductVariantCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.Variants, cancellationToken);

        var variant = new ProductVariant(product.Id, request.Sku, request.OptionName, request.PriceAdjustmentTry, request.StockQuantity);
        product.AddVariant(variant);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return variant.Id;
    }
}
