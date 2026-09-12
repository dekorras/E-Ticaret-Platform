using Dekorras.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasMany(p => p.Transactions).WithOne().HasForeignKey(t => t.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Payment.Transactions))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
