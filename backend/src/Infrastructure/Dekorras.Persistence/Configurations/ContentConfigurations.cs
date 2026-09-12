using Dekorras.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class CmsPageConfiguration : IEntityTypeConfiguration<CmsPage>
{
    public void Configure(EntityTypeBuilder<CmsPage> builder)
    {
        builder.HasIndex(p => p.Slug).IsUnique();

        builder.HasMany(p => p.Translations).WithOne().HasForeignKey(t => t.CmsPageId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(CmsPage.Translations))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> builder) => builder.HasIndex(b => new { b.Slug, b.LanguageCode }).IsUnique();
}

public class BannerZoneConfiguration : IEntityTypeConfiguration<BannerZone>
{
    public void Configure(EntityTypeBuilder<BannerZone> builder) => builder.HasIndex(z => z.Key).IsUnique();
}

public class BannerNodeConfiguration : IEntityTypeConfiguration<BannerNode>
{
    public void Configure(EntityTypeBuilder<BannerNode> builder)
    {
        builder.HasOne(n => n.Parent)
            .WithMany(n => n.Children)
            .HasForeignKey(n => n.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(BannerNode.Children))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(n => n.Contents).WithOne().HasForeignKey(c => c.BannerNodeId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(BannerNode.Contents))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(n => new { n.BannerZoneId, n.ParentId, n.SortOrder });
    }
}

public class BannerContentConfiguration : IEntityTypeConfiguration<BannerContent>
{
    public void Configure(EntityTypeBuilder<BannerContent> builder) => builder.HasIndex(c => new { c.BannerNodeId, c.SortOrder });
}
