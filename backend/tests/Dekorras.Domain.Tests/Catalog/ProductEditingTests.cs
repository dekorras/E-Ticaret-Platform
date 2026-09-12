using Dekorras.Domain.Catalog;

namespace Dekorras.Domain.Tests.Catalog;

public class ProductEditingTests
{
    private static Product CreateProduct() => new("sove-60x60", "SOVE-001", 100m, 20m, UnitOfMeasure.LinearMeter);

    [Fact]
    public void SetCategories_OncekindeOlmayanKategorileriEkler()
    {
        var product = CreateProduct();
        var categoryA = Guid.NewGuid();
        var categoryB = Guid.NewGuid();

        product.SetCategories([categoryA, categoryB]);

        Assert.Equal(2, product.ProductCategories.Count);
        Assert.Contains(product.ProductCategories, pc => pc.CategoryId == categoryA);
        Assert.Contains(product.ProductCategories, pc => pc.CategoryId == categoryB);
    }

    [Fact]
    public void SetCategories_ArtikListedeOlmayanKategoriyiKaldirir()
    {
        var product = CreateProduct();
        var categoryA = Guid.NewGuid();
        var categoryB = Guid.NewGuid();
        product.SetCategories([categoryA, categoryB]);

        product.SetCategories([categoryB]);

        Assert.Single(product.ProductCategories);
        Assert.Equal(categoryB, product.ProductCategories.Single().CategoryId);
    }

    [Fact]
    public void SetCategories_IlkKategoriPrimaryOlarakIsaretlenir()
    {
        var product = CreateProduct();
        var categoryA = Guid.NewGuid();
        var categoryB = Guid.NewGuid();

        product.SetCategories([categoryA, categoryB]);

        Assert.True(product.ProductCategories.First(pc => pc.CategoryId == categoryA).IsPrimary);
        Assert.False(product.ProductCategories.First(pc => pc.CategoryId == categoryB).IsPrimary);
    }

    [Fact]
    public void AssignBrand_MarkaAtamasiGuncellenebilir()
    {
        var product = CreateProduct();
        var brandId = Guid.NewGuid();

        product.AssignBrand(brandId);
        Assert.Equal(brandId, product.BrandId);

        product.AssignBrand(null);
        Assert.Null(product.BrandId);
    }
}
