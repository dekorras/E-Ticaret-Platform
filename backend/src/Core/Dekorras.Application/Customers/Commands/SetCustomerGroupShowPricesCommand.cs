using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Commands;

/// <summary>Bkz. plan §2.6 - "müşteri grupları: ... ürün fiyatlarını sadece belirli gruplara
/// gösterme opsiyonu mevcut". `CustomerGroup.ShowPricesOnStorefront` Faz 0/1'den beri vardı ama
/// hiçbir yerden set edilemiyordu (her zaman varsayılan `true` kalıyordu).</summary>
public sealed record SetCustomerGroupShowPricesCommand(Guid CustomerGroupId, bool ShowPricesOnStorefront) : IRequest<Unit>;

public sealed class SetCustomerGroupShowPricesCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCustomerGroupShowPricesCommand, Unit>
{
    public async Task<Unit> Handle(SetCustomerGroupShowPricesCommand request, CancellationToken cancellationToken)
    {
        var group = await unitOfWork.Repository<CustomerGroup>().GetByIdAsync(request.CustomerGroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.CustomerGroupId}' numaralı müşteri grubu bulunamadı.");

        group.SetShowPricesOnStorefront(request.ShowPricesOnStorefront);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
