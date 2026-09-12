using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Commands;

public sealed record SetCustomerActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetCustomerActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCustomerActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetCustomerActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Customer>();
        var customer = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı müşteri bulunamadı.");

        // Update(customer) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.IsActive) customer.Activate(); else customer.Deactivate();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
