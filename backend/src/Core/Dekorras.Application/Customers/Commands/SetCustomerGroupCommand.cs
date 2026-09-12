using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Commands;

/// <summary>`Customer.ChangeGroup` Faz 0/1'den beri domain'de vardı ama hiçbir yerden
/// çağrılamıyordu - bir müşteri kaydolduktan sonra sonsuza kadar "Bireysel" grubunda kalıyordu,
/// admin onu "Kurumsal" (B2B) grubuna TAŞIYAMIYORDU. Bkz. plan §2.6 - müşteri grubuna özel
/// fiyatlandırma (`ProductGroupPrice`) bu komut olmadan hiçbir gerçek müşteriye UYGULANAMAZDI.</summary>
public sealed record SetCustomerGroupCommand(Guid CustomerId, Guid CustomerGroupId) : IRequest<Unit>;

public sealed class SetCustomerGroupCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCustomerGroupCommand, Unit>
{
    public async Task<Unit> Handle(SetCustomerGroupCommand request, CancellationToken cancellationToken)
    {
        var customerRepository = unitOfWork.Repository<Customer>();
        var customer = await customerRepository.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.CustomerId}' numaralı müşteri bulunamadı.");

        var groupExists = unitOfWork.Repository<CustomerGroup>().Query().Any(g => g.Id == request.CustomerGroupId);
        if (!groupExists)
            throw new KeyNotFoundException($"'{request.CustomerGroupId}' numaralı müşteri grubu bulunamadı.");

        // Update(customer) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        customer.ChangeGroup(request.CustomerGroupId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
