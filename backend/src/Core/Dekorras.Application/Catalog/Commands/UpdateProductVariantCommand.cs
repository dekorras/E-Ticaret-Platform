using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record UpdateProductVariantCommand(Guid ProductId, Guid VariantId, string OptionName, decimal? PriceAdjustmentTry, int StockQuantity) : IRequest<Unit>;

public sealed class UpdateProductVariantCommandValidator : AbstractValidator<UpdateProductVariantCommand>
{
    public UpdateProductVariantCommandValidator()
    {
        RuleFor(x => x.OptionName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateProductVariantCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateProductVariantCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProductVariantCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.Variants, cancellationToken);

        var variant = product.Variants.FirstOrDefault(v => v.Id == request.VariantId)
            ?? throw new KeyNotFoundException($"'{request.VariantId}' numaralı varyant bulunamadı.");

        variant.UpdateDetails(request.OptionName, request.PriceAdjustmentTry, request.StockQuantity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
