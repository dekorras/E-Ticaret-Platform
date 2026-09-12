using Dekorras.Domain.Marketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.HasMany(c => c.Rules).WithOne().HasForeignKey(r => r.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Campaign.Rules))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class NewsletterSubscriberConfiguration : IEntityTypeConfiguration<NewsletterSubscriber>
{
    public void Configure(EntityTypeBuilder<NewsletterSubscriber> builder) => builder.HasIndex(s => s.Email).IsUnique();
}
