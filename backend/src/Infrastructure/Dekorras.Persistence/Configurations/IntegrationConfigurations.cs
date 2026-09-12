using Dekorras.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class IntegrationProviderConfiguration : IEntityTypeConfiguration<IntegrationProvider>
{
    public void Configure(EntityTypeBuilder<IntegrationProvider> builder)
    {
        builder.HasIndex(p => p.ProviderKey).IsUnique();

        builder.HasMany(p => p.ConfigFields).WithOne().HasForeignKey(f => f.IntegrationProviderId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(IntegrationProvider.ConfigFields))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
