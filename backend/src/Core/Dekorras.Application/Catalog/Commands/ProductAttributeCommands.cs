using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

/// <summary>Ürün özellik TÜRÜ (ör. "Renk", "Malzeme") - tüm ürünler arasında PAYLAŞILAN global bir
/// tanım, tek bir ürüne ait değildir (bkz. Domain.Catalog.ProductAttribute, `Name` üzerinde benzersiz
/// dizin zaten Faz 0/1'de kurulmuştu).</summary>
public sealed record CreateProductAttributeCommand(string Name) : IRequest<Guid>;

public sealed class CreateProductAttributeCommandValidator : AbstractValidator<CreateProductAttributeCommand>
{
    public CreateProductAttributeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateProductAttributeCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateProductAttributeCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductAttributeCommand request, CancellationToken cancellationToken)
    {
        var existing = unitOfWork.Repository<ProductAttribute>().Query().FirstOrDefault(a => a.Name == request.Name);
        if (existing is not null)
            throw new InvalidOperationException($"'{request.Name}' adlı bir özellik türü zaten var.");

        var attribute = new ProductAttribute(request.Name);
        await unitOfWork.Repository<ProductAttribute>().AddAsync(attribute, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return attribute.Id;
    }
}

/// <summary>Bir ürünün belirli bir özellik türü için değerini (yoksa) ekler, (varsa) günceller -
/// "her ürün+özellik türü çifti için tek bir değer" kuralı burada, veritabanı kısıtı olarak DEĞİL
/// uygulama katmanında uygulanır (bu turun kapsamında yeni bir migration gerektirmemesi için).</summary>
public sealed record SetProductAttributeValueCommand(Guid ProductId, Guid ProductAttributeId, string Value) : IRequest<Guid>;

public sealed class SetProductAttributeValueCommandValidator : AbstractValidator<SetProductAttributeValueCommand>
{
    public SetProductAttributeValueCommandValidator()
    {
        RuleFor(x => x.Value).NotEmpty().MaximumLength(500);
    }
}

public sealed class SetProductAttributeValueCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetProductAttributeValueCommand, Guid>
{
    public async Task<Guid> Handle(SetProductAttributeValueCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.AttributeValues, cancellationToken);

        var existingValue = product.AttributeValues.FirstOrDefault(v => v.ProductAttributeId == request.ProductAttributeId);
        if (existingValue is not null)
        {
            existingValue.UpdateValue(request.Value);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return existingValue.Id;
        }

        var value = new ProductAttributeValue(product.Id, request.ProductAttributeId, request.Value);
        product.AddAttributeValue(value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return value.Id;
    }
}

public sealed record RemoveProductAttributeValueCommand(Guid ProductId, Guid AttributeValueId) : IRequest<Unit>;

public sealed class RemoveProductAttributeValueCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveProductAttributeValueCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProductAttributeValueCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.AttributeValues, cancellationToken);

        product.RemoveAttributeValue(request.AttributeValueId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
