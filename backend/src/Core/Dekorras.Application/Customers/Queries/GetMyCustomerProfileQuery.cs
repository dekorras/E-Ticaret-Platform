using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

public sealed record CustomerAddressDto(
    Guid Id,
    string RecipientName,
    string CountryCode,
    string City,
    string AddressLine1,
    string PhoneNumber,
    string? State = null,
    string? District = null,
    string? PostalCode = null,
    string? AddressLine2 = null,
    bool IsDefaultShipping = false,
    bool IsDefaultBilling = false);

public sealed record CustomerProfileDto(Guid Id, string FullName, string Email, string? PhoneNumber, IReadOnlyCollection<CustomerAddressDto> Addresses, bool NewsletterSubscribed);

public sealed record GetMyCustomerProfileQuery(string IdentityUserId) : IRequest<CustomerProfileDto?>;

public sealed class GetMyCustomerProfileQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyCustomerProfileQuery, CustomerProfileDto?>
{
    public Task<CustomerProfileDto?> Handle(GetMyCustomerProfileQuery request, CancellationToken cancellationToken)
    {
        var customer = unitOfWork.Repository<Customer>().Query()
            .FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null) return Task.FromResult<CustomerProfileDto?>(null);

        var addresses = unitOfWork.Repository<Address>().Query()
            .Where(a => a.CustomerId == customer.Id)
            .Select(a => new CustomerAddressDto(
                a.Id, a.RecipientName, a.CountryCode, a.City, a.AddressLine1, a.PhoneNumber,
                a.State, a.District, a.PostalCode, a.AddressLine2, a.IsDefaultShipping, a.IsDefaultBilling))
            .ToList();

        return Task.FromResult<CustomerProfileDto?>(new CustomerProfileDto(customer.Id, customer.FullName, customer.Email, customer.PhoneNumber, addresses, customer.NewsletterSubscribed));
    }
}
