using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

/// <summary>Bkz. plan §2.4 - "Stoktan Düş (E/H)". `Product.TrackStock` Faz 0/1'den beri vardı
/// (varsayılan `true`) ama hiçbir yerden değiştirilemiyordu.</summary>
public sealed record SetProductTrackStockCommand(Guid ProductId, bool TrackStock) : IRequest<Unit>;

public sealed class SetProductTrackStockCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetProductTrackStockCommand, Unit>
{
    public async Task<Unit> Handle(SetProductTrackStockCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        // Update(product) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        product.SetTrackStock(request.TrackStock);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
