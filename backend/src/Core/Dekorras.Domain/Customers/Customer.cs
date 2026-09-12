using Dekorras.Domain.Common;

namespace Dekorras.Domain.Customers;

public class Customer : AuditableEntity
{
    public string IdentityUserId { get; private set; } = default!; // ASP.NET Core Identity kullanıcısına referans
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? PhoneNumber { get; private set; }
    public Guid CustomerGroupId { get; private set; }
    public bool NewsletterSubscribed { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? AffiliateCode { get; private set; }
    public Guid? ReferredByAffiliateId { get; private set; }

    private readonly List<Address> _addresses = [];
    public IReadOnlyCollection<Address> Addresses => _addresses.AsReadOnly();

    private readonly List<WishlistItem> _wishlist = [];
    public IReadOnlyCollection<WishlistItem> Wishlist => _wishlist.AsReadOnly();

    private Customer() { }

    public Customer(string identityUserId, string fullName, string email, Guid customerGroupId)
    {
        IdentityUserId = identityUserId;
        FullName = fullName;
        Email = email;
        CustomerGroupId = customerGroupId;
    }

    public void ChangeGroup(Guid customerGroupId) => CustomerGroupId = customerGroupId;
    public void UpdateContact(string fullName, string? phoneNumber)
    {
        FullName = fullName;
        PhoneNumber = phoneNumber;
    }
    public void SubscribeNewsletter() => NewsletterSubscribed = true;
    public void UnsubscribeNewsletter() => NewsletterSubscribed = false;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public void AddAddress(Address address) => _addresses.Add(address);

    public void AddToWishlist(Guid productId)
    {
        if (_wishlist.Any(w => w.ProductId == productId)) return;
        _wishlist.Add(new WishlistItem(Id, productId));
    }

    public void RemoveFromWishlist(Guid productId) => _wishlist.RemoveAll(w => w.ProductId == productId);
}

public class WishlistItem : BaseEntity
{
    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public DateTime AddedAtUtc { get; private set; } = DateTime.UtcNow;

    private WishlistItem() { }

    public WishlistItem(Guid customerId, Guid productId)
    {
        CustomerId = customerId;
        ProductId = productId;
    }
}
