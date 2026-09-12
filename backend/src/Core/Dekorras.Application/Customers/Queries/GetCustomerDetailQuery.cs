using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

public sealed record CustomerOrderSummaryDto(Guid Id, string OrderNumber, DateTime CreatedAtUtc, decimal GrandTotalTry, OrderStatus Status);

public sealed record CustomerDetailDto(
    Guid Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    Guid CustomerGroupId,
    string GroupName,
    bool IsGuest,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<CustomerAddressDto> Addresses,
    IReadOnlyCollection<CustomerOrderSummaryDto> Orders);

public sealed record GetCustomerDetailQuery(Guid Id) : IRequest<CustomerDetailDto?>;

public sealed class GetCustomerDetailQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCustomerDetailQuery, CustomerDetailDto?>
{
    public Task<CustomerDetailDto?> Handle(GetCustomerDetailQuery request, CancellationToken cancellationToken)
    {
        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.Id == request.Id);
        if (customer is null) return Task.FromResult<CustomerDetailDto?>(null);

        var groupName = unitOfWork.Repository<CustomerGroup>().Query()
            .Where(g => g.Id == customer.CustomerGroupId)
            .Select(g => g.Name)
            .FirstOrDefault() ?? "-";

        var addresses = unitOfWork.Repository<Address>().Query()
            .Where(a => a.CustomerId == customer.Id)
            .Select(a => new CustomerAddressDto(a.Id, a.RecipientName, a.CountryCode, a.City, a.AddressLine1, a.PhoneNumber))
            .ToList();

        var orders = unitOfWork.Repository<Order>().Query()
            .Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new CustomerOrderSummaryDto(o.Id, o.OrderNumber, o.CreatedAtUtc, o.GrandTotalTry, o.Status))
            .ToList();

        var dto = new CustomerDetailDto(
            customer.Id,
            customer.FullName,
            customer.Email,
            customer.PhoneNumber,
            customer.CustomerGroupId,
            groupName,
            customer.IdentityUserId.StartsWith("guest-"),
            customer.IsActive,
            customer.CreatedAtUtc,
            addresses,
            orders);

        return Task.FromResult<CustomerDetailDto?>(dto);
    }
}
