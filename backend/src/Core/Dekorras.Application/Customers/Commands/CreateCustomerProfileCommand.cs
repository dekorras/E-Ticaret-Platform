using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Commands;

/// <summary>Bir Identity kullanıcısı Storefront'ta kayıt olduğunda çağrılır - kimlik doğrulama
/// (UserManager/SignInManager) Presentation katmanında (AccountController) yapılır, bu komut
/// yalnızca o kullanıcıya karşılık gelen iş-alanı Customer kaydını oluşturur.</summary>
public sealed record CreateCustomerProfileCommand(string IdentityUserId, string FullName, string Email) : IRequest<Guid>;

public sealed class CreateCustomerProfileCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateCustomerProfileCommand, Guid>
{
    public async Task<Guid> Handle(CreateCustomerProfileCommand request, CancellationToken cancellationToken)
    {
        var existing = unitOfWork.Repository<Customer>().Query()
            .FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (existing is not null)
            return existing.Id;

        var customerGroupId = unitOfWork.Repository<CustomerGroup>().Query()
            .Where(g => g.Name == "Bireysel")
            .Select(g => (Guid?)g.Id)
            .FirstOrDefault() ?? throw new InvalidOperationException("'Bireysel' müşteri grubu bulunamadı.");

        var customer = new Customer(request.IdentityUserId, request.FullName, request.Email, customerGroupId);
        await unitOfWork.Repository<Customer>().AddAsync(customer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return customer.Id;
    }
}
