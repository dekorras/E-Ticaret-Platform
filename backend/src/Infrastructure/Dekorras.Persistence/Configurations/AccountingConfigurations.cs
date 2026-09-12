using Dekorras.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.HasMany(a => a.Transactions).WithOne().HasForeignKey(t => t.LedgerAccountId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(LedgerAccount.Transactions))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasIndex(i => i.InvoiceNumber).IsUnique();

        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Invoice.Lines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.HasIndex(q => q.QuoteNumber).IsUnique();

        builder.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Quote.Lines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class WaybillConfiguration : IEntityTypeConfiguration<Waybill>
{
    public void Configure(EntityTypeBuilder<Waybill> builder) => builder.HasIndex(w => w.WaybillNumber).IsUnique();
}
