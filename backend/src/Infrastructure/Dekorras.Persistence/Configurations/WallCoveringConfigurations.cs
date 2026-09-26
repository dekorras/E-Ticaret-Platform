using Dekorras.Domain.WallCovering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.HasIndex(m => m.Code).IsUnique();
        builder.Property(m => m.Code).HasMaxLength(50);
        builder.Property(m => m.Name).HasMaxLength(150);
        builder.Property(m => m.FireRating).HasMaxLength(30);
    }
}

public class ProductMaterialOverrideConfiguration : IEntityTypeConfiguration<ProductMaterialOverride>
{
    public void Configure(EntityTypeBuilder<ProductMaterialOverride> builder) =>
        builder.HasIndex(o => new { o.ProductId, o.MaterialId }).IsUnique();
}

public class WallpaperProfileConfiguration : IEntityTypeConfiguration<WallpaperProfile>
{
    public void Configure(EntityTypeBuilder<WallpaperProfile> builder)
    {
        builder.HasIndex(p => p.ProductId).IsUnique();
        builder.Property(p => p.DominantColors).HasMaxLength(100);
        builder.Property(p => p.OriginalImageKey).HasMaxLength(500);
        builder.Property(p => p.ThumbUrl).HasMaxLength(500);
        builder.Property(p => p.ListUrl).HasMaxLength(500);
        builder.Property(p => p.PreviewUrl).HasMaxLength(500);
        builder.Property(p => p.SceneThumbUrl).HasMaxLength(500);
        builder.HasIndex(p => new { p.IsEnabled, p.PopularityScore });
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasIndex(t => new { t.Group, t.Value }).IsUnique();
        builder.Property(t => t.Value).HasMaxLength(80);
        builder.Property(t => t.Label).HasMaxLength(120);
        builder.Property(t => t.Hex).HasMaxLength(7);
        builder.Ignore(t => t.Key);
    }
}

public class ProductTagConfiguration : IEntityTypeConfiguration<ProductTag>
{
    public void Configure(EntityTypeBuilder<ProductTag> builder)
    {
        builder.HasIndex(pt => new { pt.ProductId, pt.TagId }).IsUnique();
        builder.HasIndex(pt => pt.TagId);
    }
}

public class RoomSceneConfiguration : IEntityTypeConfiguration<RoomScene>
{
    public void Configure(EntityTypeBuilder<RoomScene> builder)
    {
        builder.Ignore(s => s.WallQuad);
        builder.Ignore(s => s.IsUserScene);
        builder.Property(s => s.Name).HasMaxLength(150);
        builder.Property(s => s.OwnerKey).HasMaxLength(80);
        builder.HasIndex(s => s.OwnerKey);
    }
}

public class RoomPreviewRenderConfiguration : IEntityTypeConfiguration<RoomPreviewRender>
{
    public void Configure(EntityTypeBuilder<RoomPreviewRender> builder)
    {
        builder.Property(r => r.OwnerKey).HasMaxLength(80);
        builder.HasIndex(r => new { r.OwnerKey, r.CreatedAtUtc });
    }
}

public class TryOnListConfiguration : IEntityTypeConfiguration<TryOnList>
{
    public void Configure(EntityTypeBuilder<TryOnList> builder)
    {
        builder.Property(l => l.OwnerKey).HasMaxLength(80);
        builder.Property(l => l.ShareToken).HasMaxLength(40);
        builder.HasIndex(l => l.OwnerKey).IsUnique();
        builder.HasIndex(l => l.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");

        builder.HasMany(l => l.Items).WithOne().HasForeignKey(i => i.TryOnListId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(TryOnList.Items))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class DesignRequestConfiguration : IEntityTypeConfiguration<DesignRequest>
{
    public void Configure(EntityTypeBuilder<DesignRequest> builder)
    {
        builder.Property(r => r.FullName).HasMaxLength(200);
        builder.Property(r => r.Email).HasMaxLength(256);
        builder.Property(r => r.Message).HasMaxLength(4000);
        builder.HasIndex(r => new { r.Status, r.DueAtUtc });

        builder.HasMany(r => r.Attachments).WithOne().HasForeignKey(a => a.DesignRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(DesignRequest.Attachments))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ProductionProofConfiguration : IEntityTypeConfiguration<ProductionProof>
{
    public void Configure(EntityTypeBuilder<ProductionProof> builder)
    {
        builder.Property(p => p.Token).HasMaxLength(40);
        builder.HasIndex(p => p.Token).IsUnique();
        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.OrderItemId);
    }
}

public class ProductionFileConfiguration : IEntityTypeConfiguration<ProductionFile>
{
    public void Configure(EntityTypeBuilder<ProductionFile> builder)
    {
        builder.HasIndex(f => f.OrderId);
        builder.HasIndex(f => f.OrderItemId).IsUnique();
    }
}

public class EmbedClientConfiguration : IEntityTypeConfiguration<EmbedClient>
{
    public void Configure(EntityTypeBuilder<EmbedClient> builder)
    {
        builder.Property(c => c.PublicKey).HasMaxLength(40);
        builder.HasIndex(c => c.PublicKey).IsUnique();
        builder.Ignore(c => c.Origins);
        builder.Ignore(c => c.ImageHosts);
    }
}

public class ExternalImageConfiguration : IEntityTypeConfiguration<ExternalImage>
{
    public void Configure(EntityTypeBuilder<ExternalImage> builder)
    {
        builder.Property(i => i.SourceUrlHash).HasMaxLength(64);
        builder.HasIndex(i => new { i.EmbedClientId, i.SourceUrlHash }).IsUnique();
    }
}

public class WallPreviewEventConfiguration : IEntityTypeConfiguration<WallPreviewEvent>
{
    public void Configure(EntityTypeBuilder<WallPreviewEvent> builder)
    {
        builder.Property(e => e.EventType).HasMaxLength(60);
        builder.Property(e => e.Source).HasMaxLength(30);
        builder.Property(e => e.VisitorKey).HasMaxLength(80);
        builder.HasIndex(e => new { e.EventType, e.OccurredAtUtc });
    }
}
