using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record AddProductImageCommand(Guid ProductId, Stream Content, string FileName, string ContentType) : IRequest<Guid>;

public sealed class AddProductImageCommandValidator : AbstractValidator<AddProductImageCommand>
{
    public AddProductImageCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType).Must(t => t.StartsWith("image/")).WithMessage("Yalnızca görsel dosyaları yüklenebilir.");
    }
}

public sealed class AddProductImageCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage) : IRequestHandler<AddProductImageCommand, Guid>
{
    public async Task<Guid> Handle(AddProductImageCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        // Yeni görselin ilk mi (dolayısıyla otomatik ana görsel mi) olacağını doğru saptamak için
        // Images yüklenir - bkz. IRepository<T>.LoadCollectionAsync.
        await repository.LoadCollectionAsync(product, p => p.Images, cancellationToken);

        var url = await fileStorage.UploadAsync("product-images", request.FileName, request.Content, request.ContentType, cancellationToken);
        var displayOrder = product.Images.Count;
        product.AddImage(url, displayOrder);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Images.Last().Id;
    }
}
