using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

/// <summary>Bkz. plan §2.4 - "Stok Dışı Durumu (2-3 gün içinde / Ön Sipariş / Stokta var / Stokta
/// yok)". `Domain.Catalog.Product.StockAvailability` Faz 0/1'den beri tüm 4 durumu modelliyordu ama
/// `UpdateStock` yalnızca InStock/OutOfStock'u otomatik türetiyordu - PreOrder/ArrivesInDays hiçbir
/// yerden HİÇ seçilemiyordu (kısmi bir orphaned alan).</summary>
public sealed record SetProductStockAvailabilityCommand(Guid ProductId, StockAvailability Status) : IRequest<Unit>;

public sealed class SetProductStockAvailabilityCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetProductStockAvailabilityCommand, Unit>
{
    public async Task<Unit> Handle(SetProductStockAvailabilityCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        // Update(product) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        product.SetStockAvailability(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
