using Dekorras.Domain.Catalog;

namespace Dekorras.Domain.Tests.Catalog;

public class ProductTests
{
    private static Product CreateProduct() => new("sove-60x60", "SOVE-001", 100m, 20m, UnitOfMeasure.LinearMeter);

    [Fact]
    public void MinimumSatisAdedi_SifirinAltindaOlamaz()
    {
        var product = CreateProduct();

        product.SetMinimumOrderQuantity(0);

        Assert.Equal(1, product.MinimumOrderQuantity);
    }

    [Fact]
    public void GrupFiyati_TanimliDegilse_TemelFiyatDoner()
    {
        var product = CreateProduct();

        Assert.Equal(100m, product.GetPriceFor(Guid.NewGuid()));
    }

    [Fact]
    public void GrupFiyati_TanimliyseOFiyatDoner()
    {
        var product = CreateProduct();
        var kurumsalGrupId = Guid.NewGuid();
        product.SetGroupPrice(kurumsalGrupId, 85m);

        Assert.Equal(85m, product.GetPriceFor(kurumsalGrupId));
        Assert.Equal(100m, product.GetPriceFor(Guid.NewGuid())); // başka grup etkilenmez
    }

    [Fact]
    public void StokGuncelleme_SifirdaStokDisiOlarakIsaretlenir()
    {
        var product = CreateProduct();

        product.UpdateStock(0);

        Assert.Equal(StockAvailability.OutOfStock, product.StockAvailability);
    }
}
