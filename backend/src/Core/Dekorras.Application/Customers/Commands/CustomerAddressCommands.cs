using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Commands;

/// <summary>Storefront'ta "Adreslerim" (adres defteri) - `Address.SetAsDefaultShipping`/
/// `SetAsDefaultBilling` Faz 0/1'den beri vardı ama hiçbir yerden çağrılamıyordu; `PlaceOrderCommand`
/// her siparişte YENİ bir adres satırı oluşturuyordu, müşterinin adresleri asla yeniden
/// KULLANILAMIYOR/yönetilemiyordu (checkout'a bu adreslerden seçim yapma BİLİNÇLİ OLARAK bu turun
/// kapsamı dışında - ayrı, daha büyük bir karar, bkz. README).</summary>
public sealed record AddCustomerAddressCommand(
    string IdentityUserId,
    string RecipientName,
    string CountryCode,
    string City,
    string AddressLine1,
    string PhoneNumber,
    string? State = null,
    string? District = null,
    string? PostalCode = null,
    string? AddressLine2 = null) : IRequest<Guid>;

public sealed class AddCustomerAddressCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddCustomerAddressCommand, Guid>
{
    public async Task<Guid> Handle(AddCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId)
            ?? throw new KeyNotFoundException("Müşteri bulunamadı.");

        var address = new Address(customer.Id, request.RecipientName, request.CountryCode, request.City,
            request.AddressLine1, request.PhoneNumber, request.State, request.District, request.PostalCode, request.AddressLine2);
        await unitOfWork.Repository<Address>().AddAsync(address, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return address.Id;
    }
}

public sealed record RemoveCustomerAddressCommand(Guid AddressId, string IdentityUserId) : IRequest<Unit>;

public sealed class RemoveCustomerAddressCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveCustomerAddressCommand, Unit>
{
    public async Task<Unit> Handle(RemoveCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        var addressRepository = unitOfWork.Repository<Address>();
        var address = await addressRepository.GetByIdAsync(request.AddressId, cancellationToken)
            ?? throw new KeyNotFoundException("Adres bulunamadı.");

        // Sahiplik kontrolü - aksi halde bir müşteri başkasının adres ID'sini tahmin ederek onu
        // silebilirdi (bkz. OrderDetail sahiplik kontrolündeki aynı gerekçe, devamı 12).
        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null || address.CustomerId != customer.Id)
            throw new UnauthorizedAccessException("Bu adres size ait değil.");

        addressRepository.Remove(address);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record SetDefaultShippingAddressCommand(Guid AddressId, string IdentityUserId) : IRequest<Unit>;

public sealed class SetDefaultShippingAddressCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetDefaultShippingAddressCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultShippingAddressCommand request, CancellationToken cancellationToken)
    {
        var addressRepository = unitOfWork.Repository<Address>();
        var target = await addressRepository.GetByIdAsync(request.AddressId, cancellationToken)
            ?? throw new KeyNotFoundException("Adres bulunamadı.");

        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null || target.CustomerId != customer.Id)
            throw new UnauthorizedAccessException("Bu adres size ait değil.");

        // Aynı anda yalnızca BİR adres varsayılan olabilir - müşterinin diğer adreslerindeki
        // bayrak önce kaldırılır.
        var otherDefaults = addressRepository.Query().Where(a => a.CustomerId == customer.Id && a.Id != target.Id && a.IsDefaultShipping).ToList();
        foreach (var other in otherDefaults) other.UnsetAsDefaultShipping();

        target.SetAsDefaultShipping();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record SetDefaultBillingAddressCommand(Guid AddressId, string IdentityUserId) : IRequest<Unit>;

public sealed class SetDefaultBillingAddressCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetDefaultBillingAddressCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultBillingAddressCommand request, CancellationToken cancellationToken)
    {
        var addressRepository = unitOfWork.Repository<Address>();
        var target = await addressRepository.GetByIdAsync(request.AddressId, cancellationToken)
            ?? throw new KeyNotFoundException("Adres bulunamadı.");

        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null || target.CustomerId != customer.Id)
            throw new UnauthorizedAccessException("Bu adres size ait değil.");

        var otherDefaults = addressRepository.Query().Where(a => a.CustomerId == customer.Id && a.Id != target.Id && a.IsDefaultBilling).ToList();
        foreach (var other in otherDefaults) other.UnsetAsDefaultBilling();

        target.SetAsDefaultBilling();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
