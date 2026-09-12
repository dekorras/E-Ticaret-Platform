using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record RemoveProductImageCommand(Guid ProductId, Guid ImageId) : IRequest<Unit>;

public sealed class RemoveProductImageCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage) : IRequestHandler<RemoveProductImageCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProductImageCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.Images, cancellationToken);

        var image = product.Images.FirstOrDefault(i => i.Id == request.ImageId);
        if (image is null) return Unit.Value;

        var url = image.Url;
        product.RemoveImage(request.ImageId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await fileStorage.DeleteAsync(url, cancellationToken);

        return Unit.Value;
    }
}
