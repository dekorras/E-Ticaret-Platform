using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

/// <summary>`GetMyCustomerGroupIdQuery`'nin CustomerId sürümü - sepeti (`Cart.CustomerId`, bkz. plan
/// §2.2 "misafir alışverişi") giriş yapmış bir müşteriye bağlamak için kullanılır. Misafirler için
/// `null` döner.</summary>
public sealed record GetMyCustomerIdQuery(string? IdentityUserId) : IRequest<Guid?>;

public sealed class GetMyCustomerIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyCustomerIdQuery, Guid?>
{
    public Task<Guid?> Handle(GetMyCustomerIdQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.IdentityUserId))
            return Task.FromResult<Guid?>(null);

        var customerId = unitOfWork.Repository<Customer>().Query()
            .Where(c => c.IdentityUserId == request.IdentityUserId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefault();

        return Task.FromResult(customerId);
    }
}
