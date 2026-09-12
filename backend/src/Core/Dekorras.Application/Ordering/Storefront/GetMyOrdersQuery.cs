using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record MyOrderListItemDto(Guid Id, string OrderNumber, DateTime CreatedAtUtc, decimal GrandTotalTry, OrderStatus Status);

/// <summary>Hesabım > Siparişlerim sayfası - kayıtlı bir müşterinin geçmiş siparişlerini listeler.
/// Misafir siparişleri (Customer bir Identity hesabına bağlı değilse) burada görünmez.</summary>
public sealed record GetMyOrdersQuery(string IdentityUserId) : IRequest<IReadOnlyCollection<MyOrderListItemDto>>;

public sealed class GetMyOrdersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyOrdersQuery, IReadOnlyCollection<MyOrderListItemDto>>
{
    public Task<IReadOnlyCollection<MyOrderListItemDto>> Handle(GetMyOrdersQuery request, CancellationToken cancellationToken)
    {
        var customerId = unitOfWork.Repository<Customer>().Query()
            .Where(c => c.IdentityUserId == request.IdentityUserId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefault();

        if (customerId is null)
            return Task.FromResult<IReadOnlyCollection<MyOrderListItemDto>>([]);

        var orders = unitOfWork.Repository<Order>().Query()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new MyOrderListItemDto(o.Id, o.OrderNumber, o.CreatedAtUtc, o.GrandTotalTry, o.Status))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<MyOrderListItemDto>>(orders);
    }
}
