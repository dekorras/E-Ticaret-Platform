using Dekorras.Domain.Ordering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasIndex(o => o.OrderNumber).IsUnique();

        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Order.Items))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Order.StatusHistory))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.HasIndex(c => c.SessionKey);

        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Cart.Items))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder) => builder.HasIndex(c => c.Code).IsUnique();
}

public class GiftVoucherConfiguration : IEntityTypeConfiguration<GiftVoucher>
{
    public void Configure(EntityTypeBuilder<GiftVoucher> builder) => builder.HasIndex(g => g.Code).IsUnique();
}
