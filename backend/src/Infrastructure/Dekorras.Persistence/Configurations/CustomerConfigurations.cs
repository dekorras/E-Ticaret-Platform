using Dekorras.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasIndex(c => c.Email).IsUnique();
        builder.HasIndex(c => c.IdentityUserId).IsUnique();

        builder.HasMany(c => c.Addresses).WithOne().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Customer.Addresses))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(c => c.Wishlist).WithOne().HasForeignKey(w => w.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Customer.Wishlist))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class CustomerGroupConfiguration : IEntityTypeConfiguration<CustomerGroup>
{
    public void Configure(EntityTypeBuilder<CustomerGroup> builder) => builder.HasIndex(g => g.Name).IsUnique();
}
