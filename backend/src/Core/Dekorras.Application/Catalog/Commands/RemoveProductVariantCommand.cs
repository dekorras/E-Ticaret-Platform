using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record RemoveProductVariantCommand(Guid ProductId, Guid VariantId) : IRequest<Unit>;

public sealed class RemoveProductVariantCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveProductVariantCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProductVariantCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.Variants, cancellationToken);

        product.RemoveVariant(request.VariantId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
