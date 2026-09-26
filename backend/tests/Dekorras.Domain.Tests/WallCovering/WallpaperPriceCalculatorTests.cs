using Dekorras.Domain.WallCovering;

namespace Dekorras.Domain.Tests.WallCovering;

public class WallpaperPriceCalculatorTests
{
    private static readonly MaterialPricing Textured = new("textured", "Dokulu", 849m, 100m, 5m, 1m);
    private static readonly MaterialPricing Plain = new("plain", "Dokusuz", 699m, 100m, 5m, 1m);

    [Fact]
    public void StandartOlcu_PayDahilFaturalanir_PanelSayisiUretimEnindenHesaplanir()
    {
        var price = WallpaperPriceCalculator.Calculate(400m, 200m, 1, Textured);

        Assert.Equal(405m, price.ProductionWidthCm);
        Assert.Equal(205m, price.ProductionHeightCm);
        Assert.Equal(8m, price.AreaM2);
        Assert.Equal(8.3025m, price.BilledAreaM2);
        Assert.Equal(5, price.PanelCount);
        Assert.Equal(7048.82m, price.UnitPrice); // 8,3025 × 849 = 7048,8225
        Assert.Equal(7048.82m, price.LineTotal);
    }

    [Fact]
    public void MinimumOlcu_MinimumFaturaAlaninaYuvarlanir()
    {
        var price = WallpaperPriceCalculator.Calculate(10m, 10m, 1, Plain);

        Assert.Equal(0.01m, price.AreaM2);
        Assert.Equal(1m, price.BilledAreaM2);
        Assert.Equal(699m, price.UnitPrice);
        Assert.Equal(1, price.PanelCount);
    }

    [Theory]
    [InlineData(95.0, 1)]  // 95 + 5 = 100 → tam sınırda tek panel
    [InlineData(95.1, 2)]  // 100,1 → ikinci panel gerekir
    [InlineData(195.0, 2)]
    [InlineData(195.1, 3)]
    public void PanelEsigi_TamSinirdaDogruHesaplanir(double widthCm, int expectedPanels)
    {
        var price = WallpaperPriceCalculator.Calculate((decimal)widthCm, 250m, 1, Plain);
        Assert.Equal(expectedPanels, price.PanelCount);
    }

    [Fact]
    public void ChargeBleedKapali_PayFiyataYansimaz_AmaPanelSayisindaKullanilir()
    {
        var price = WallpaperPriceCalculator.Calculate(400m, 200m, 1, Textured, chargeBleed: false);

        Assert.Equal(8m, price.BilledAreaM2);
        Assert.Equal(6792m, price.UnitPrice);
        Assert.Equal(5, price.PanelCount);
        Assert.False(price.BleedCharged);
    }

    [Fact]
    public void Adet_BirimFiyatYuvarlandiktanSonraCarpilir()
    {
        var price = WallpaperPriceCalculator.Calculate(400m, 200m, 3, Textured);
        Assert.Equal(7048.82m * 3, price.LineTotal);
    }

    [Fact]
    public void Yuvarlama_AwayFromZeroKullanir()
    {
        // 1 m² (minimum) × 10,005 → banker's rounding 10,00 verirdi, AwayFromZero 10,01
        var material = new MaterialPricing("x", "X", 10.005m, 100m, 5m, 1m);
        var price = WallpaperPriceCalculator.Calculate(10m, 10m, 1, material);
        Assert.Equal(10.01m, price.UnitPrice);
    }

    [Fact]
    public void GirilenOlcu_TekOndalagaYuvarlanir()
    {
        var price = WallpaperPriceCalculator.Calculate(100.04m, 100.05m, 1, Plain);
        Assert.Equal(100.0m, price.WidthCm);
        Assert.Equal(100.1m, price.HeightCm);
    }

    [Fact]
    public void GecersizAdet_Reddedilir()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WallpaperPriceCalculator.Calculate(100m, 100m, 0, Plain));
    }

    [Fact]
    public void UrunBazliFiyatIstisnasi_MalzemeFiyatininYerineGecer()
    {
        var material = new Material("plain", "Dokusuz", 699m, 100m, 330m, 180, "B-s1,d0", false, true, 1);
        var pricing = MaterialPricing.From(material, overridePricePerM2: 500m);

        var price = WallpaperPriceCalculator.Calculate(10m, 10m, 1, pricing);
        Assert.Equal(500m, price.UnitPrice);
    }
}
