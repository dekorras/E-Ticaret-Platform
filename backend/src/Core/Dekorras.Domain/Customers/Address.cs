using Dekorras.Domain.Common;

namespace Dekorras.Domain.Customers;

public class Address : AuditableEntity
{
    public Guid CustomerId { get; private set; }
    public string RecipientName { get; private set; } = default!;
    public string CountryCode { get; private set; } = default!; // ISO 3166-1 alpha-2
    public string? State { get; private set; }
    public string City { get; private set; } = default!;
    public string? District { get; private set; }
    public string? PostalCode { get; private set; }
    public string AddressLine1 { get; private set; } = default!;
    public string? AddressLine2 { get; private set; }
    public string PhoneNumber { get; private set; } = default!;
    public bool IsDefaultShipping { get; private set; }
    public bool IsDefaultBilling { get; private set; }
    public string? TaxOffice { get; private set; } // Kurumsal fatura adresi için
    public string? TaxNumber { get; private set; }

    private Address() { }

    public Address(Guid customerId, string recipientName, string countryCode, string city,
        string addressLine1, string phoneNumber, string? state = null, string? district = null,
        string? postalCode = null, string? addressLine2 = null)
    {
        CustomerId = customerId;
        RecipientName = recipientName;
        CountryCode = countryCode;
        City = city;
        AddressLine1 = addressLine1;
        PhoneNumber = phoneNumber;
        State = state;
        District = district;
        PostalCode = postalCode;
        AddressLine2 = addressLine2;
    }

    public void SetAsDefaultShipping() => IsDefaultShipping = true;
    public void SetAsDefaultBilling() => IsDefaultBilling = true;
    public void UnsetAsDefaultShipping() => IsDefaultShipping = false;
    public void UnsetAsDefaultBilling() => IsDefaultBilling = false;
    public void SetTaxInfo(string? taxOffice, string? taxNumber)
    {
        TaxOffice = taxOffice;
        TaxNumber = taxNumber;
    }
}
