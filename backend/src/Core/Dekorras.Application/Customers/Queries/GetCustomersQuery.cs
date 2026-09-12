using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

public sealed record CustomerListItemDto(
    Guid Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    string GroupName,
    bool IsGuest,
    bool IsActive,
    DateTime CreatedAtUtc,
    int OrderCount);

/// <summary>Admin panelinin Müşteriler listesi. Misafir siparişlerinden otomatik oluşturulan
/// Customer kayıtları (IdentityUserId "guest-" ile başlar) "Misafir" olarak işaretlenir - bunlar
/// gerçek bir üye hesabına sahip değildir (bkz. plan §2.2 "misafir alışverişi").</summary>
public sealed record GetCustomersQuery(string? SearchText = null) : IRequest<IReadOnlyCollection<CustomerListItemDto>>;

public sealed class GetCustomersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCustomersQuery, IReadOnlyCollection<CustomerListItemDto>>
{
    public Task<IReadOnlyCollection<CustomerListItemDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<Customer>().Query();

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.Trim();
            query = query.Where(c => c.FullName.Contains(search) || c.Email.Contains(search));
        }

        var customers = query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Join(unitOfWork.Repository<CustomerGroup>().Query(),
                c => c.CustomerGroupId,
                g => g.Id,
                (c, g) => new { Customer = c, GroupName = g.Name })
            .ToList();

        var orderCounts = unitOfWork.Repository<Order>().Query()
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.CustomerId, x => x.Count);

        var result = customers
            .Select(x => new CustomerListItemDto(
                x.Customer.Id,
                x.Customer.FullName,
                x.Customer.Email,
                x.Customer.PhoneNumber,
                x.GroupName,
                x.Customer.IdentityUserId.StartsWith("guest-"),
                x.Customer.IsActive,
                x.Customer.CreatedAtUtc,
                orderCounts.GetValueOrDefault(x.Customer.Id)))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CustomerListItemDto>>(result);
    }
}
