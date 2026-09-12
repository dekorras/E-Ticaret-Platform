using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record SetPrimaryProductImageCommand(Guid ProductId, Guid ImageId) : IRequest<Unit>;

public sealed class SetPrimaryProductImageCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetPrimaryProductImageCommand, Unit>
{
    public async Task<Unit> Handle(SetPrimaryProductImageCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.Images, cancellationToken);

        product.SetPrimaryImage(request.ImageId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
