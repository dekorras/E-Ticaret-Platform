using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

/// <summary>Bkz. plan §2.4 - "Bağlantılar" sekmesi: "ilgili ürünler". Storefront'ta ürün detay
/// sayfasında "Benzer Ürünler" bölümünü besler.</summary>
public sealed record AddRelatedProductCommand(Guid ProductId, Guid RelatedProductId) : IRequest<Unit>;

public sealed class AddRelatedProductCommandValidator : AbstractValidator<AddRelatedProductCommand>
{
    public AddRelatedProductCommandValidator()
    {
        RuleFor(x => x).Must(x => x.ProductId != x.RelatedProductId).WithMessage("Bir ürün kendisiyle ilişkilendirilemez.");
    }
}

public sealed class AddRelatedProductCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddRelatedProductCommand, Unit>
{
    public async Task<Unit> Handle(AddRelatedProductCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        var relatedExists = repository.Query().Any(p => p.Id == request.RelatedProductId);
        if (!relatedExists)
            throw new KeyNotFoundException($"'{request.RelatedProductId}' numaralı ilişkilendirilecek ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.RelatedProducts, cancellationToken);
        product.AddRelatedProduct(request.RelatedProductId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record RemoveRelatedProductCommand(Guid ProductId, Guid RelatedProductId) : IRequest<Unit>;

public sealed class RemoveRelatedProductCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveRelatedProductCommand, Unit>
{
    public async Task<Unit> Handle(RemoveRelatedProductCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.RelatedProducts, cancellationToken);
        product.RemoveRelatedProduct(request.RelatedProductId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
