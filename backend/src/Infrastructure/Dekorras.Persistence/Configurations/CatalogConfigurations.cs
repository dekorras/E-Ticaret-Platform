using Dekorras.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasIndex(c => c.Slug).IsUnique();

        builder.HasOne(c => c.ParentCategory)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Category.Children))!.SetPropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field);

        builder.HasMany(c => c.Translations)
            .WithOne()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Category.Translations))!.SetPropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field);
    }
}

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.HasIndex(b => b.Slug).IsUnique();
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => p.ProductCode).IsUnique();

        builder.HasOne(p => p.Brand)
            .WithMany()
            .HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.SetNull);

        ConfigureOwnedCollection(builder, p => p.Translations, nameof(Product.Translations));
        ConfigureOwnedCollection(builder, p => p.ProductCategories, nameof(Product.ProductCategories));
        ConfigureOwnedCollection(builder, p => p.Images, nameof(Product.Images));
        ConfigureOwnedCollection(builder, p => p.Videos, nameof(Product.Videos));
        ConfigureOwnedCollection(builder, p => p.Variants, nameof(Product.Variants));
        ConfigureOwnedCollection(builder, p => p.AttributeValues, nameof(Product.AttributeValues));
        ConfigureOwnedCollection(builder, p => p.QuantityDiscounts, nameof(Product.QuantityDiscounts));
        ConfigureOwnedCollection(builder, p => p.GroupPrices, nameof(Product.GroupPrices));
        ConfigureOwnedCollection(builder, p => p.RelatedProducts, nameof(Product.RelatedProducts));
    }

    private static void ConfigureOwnedCollection<TEntity, TDependent>(
        EntityTypeBuilder<TEntity> builder,
        Func<TEntity, IReadOnlyCollection<TDependent>> _,
        string navigationName) where TEntity : class where TDependent : class
    {
        builder.HasMany<TDependent>(navigationName)
            .WithOne()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(navigationName)!.SetPropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field);
    }
}

public class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> builder)
    {
        builder.HasIndex(a => a.Name).IsUnique();
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder) => builder.HasIndex(v => v.Sku).IsUnique();
}
