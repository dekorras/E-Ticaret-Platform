using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Domain.Tests.WallCovering;

public class WallDimensionsTests
{
    [Theory]
    [InlineData(3.0, LengthUnit.M, 300.0)]
    [InlineData(120.0, LengthUnit.In, 304.8)]
    [InlineData(10.0, LengthUnit.Ft, 304.8)]
    [InlineData(1.0, LengthUnit.In, 2.5)]   // 2,54 → 1 ondalık
    [InlineData(250.0, LengthUnit.Cm, 250.0)]
    public void BirimDonusumu_CmyeTekOndalikla(double value, LengthUnit unit, double expectedCm)
    {
        Assert.Equal((decimal)expectedCm, WallDimensions.ToCm((decimal)value, unit));
    }

    [Theory]
    [InlineData(304.8, LengthUnit.In, 120.0)]
    [InlineData(304.8, LengthUnit.Ft, 10.0)]
    [InlineData(300.0, LengthUnit.M, 3.0)]
    public void BirimDegisince_DegerKorunur(double cm, LengthUnit unit, double expected)
    {
        Assert.Equal((decimal)expected, WallDimensions.FromCm((decimal)cm, unit));
    }

    [Theory]
    [InlineData("3,00", 3.0)]
    [InlineData("3.00", 3.0)]
    [InlineData(" 2,5 ", 2.5)]
    [InlineData("400", 400.0)]
    public void OndalikAyirici_VirgulVeNoktaKabulEdilir(string input, double expected)
    {
        Assert.True(WallDimensions.TryParseLength(input, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    [InlineData("-5")]
    [InlineData("0")]
    public void GecersizGiris_Reddedilir(string input)
    {
        Assert.False(WallDimensions.TryParseLength(input, out _));
    }

    [Fact]
    public void Dogrulama_SinirDegerleri()
    {
        Assert.Empty(WallDimensions.Validate(10m, 10m, 330m));
        Assert.Empty(WallDimensions.Validate(2000m, 330m, 330m));

        var errors = WallDimensions.Validate(9.9m, 330.1m, 330m);
        Assert.Contains("width", errors.Keys);
        Assert.Contains("height", errors.Keys);
        Assert.Contains("en az 10 cm", errors["width"]);

        Assert.Contains("width", WallDimensions.Validate(2000.1m, 100m, 330m).Keys);
    }

    [Fact]
    public void KonfigurasyonHash_BirimdenBagimsiz_MalzemeyeDuyarli()
    {
        var inCm = new WallConfiguration(300m, 250m, "plain", LengthUnit.Cm);
        var inM = new WallConfiguration(WallDimensions.ToCm(3m, LengthUnit.M), WallDimensions.ToCm(2.5m, LengthUnit.M), "plain", LengthUnit.M);
        var other = new WallConfiguration(300m, 250m, "textured");

        Assert.Equal(inCm.Hash(), inM.Hash());
        Assert.NotEqual(inCm.Hash(), other.Hash());
    }

    [Fact]
    public void Konfigurasyon_JsonGidisDonusu_EsitNesneVerir()
    {
        var config = new WallConfiguration(400m, 200m, "textured", LengthUnit.Cm, FitMode.Crop, true, ImageFilter.Grayscale, new CropRect(0.1m, 0.05m, 0.8m, 0.9m));
        var roundTrip = WallConfiguration.FromJson(config.ToJson());

        Assert.Equal(config, roundTrip);
        Assert.Equal(config.Hash(), roundTrip!.Hash());
    }

    [Fact]
    public void EsnetModu_KirpmaAlaniniAtar()
    {
        var config = new WallConfiguration(400m, 200m, "plain", fit: FitMode.Stretch, crop: new CropRect(0.1m, 0.1m, 0.5m, 0.5m));
        Assert.Null(config.Crop);
    }

    [Theory]
    [InlineData("0.1,0.05,0.8,0.9", true)]
    [InlineData("0,0,1,1", true)]
    [InlineData("0.5,0,0.6,1", false)] // taşar
    [InlineData("0.1,0.1,0.8", false)]
    [InlineData("a,b,c,d", false)]
    public void KirpmaAlaniAyristirma(string value, bool valid)
    {
        Assert.Equal(valid, CropRect.TryParse(value, out _));
    }

    [Fact]
    public void Sepet_AyniKonfigurasyonAdetArtirir_FarkliKonfigurasyonAyriSatir_DuzUrunleBirlesmez()
    {
        var cart = new Cart("oturum");
        var productId = Guid.NewGuid();

        cart.AddConfiguredItem(productId, "{}", "hash-a", "{}", 1, 1000m);
        cart.AddConfiguredItem(productId, "{}", "hash-a", "{}", 2, 1000m);
        cart.AddConfiguredItem(productId, "{}", "hash-b", "{}", 1, 1500m);
        cart.AddOrUpdateItem(productId, null, 1, 99m);

        Assert.Equal(3, cart.Items.Count);
        Assert.Equal(3, cart.Items.Single(i => i.ConfigHash == "hash-a").Quantity);

        cart.RemoveItem(productId, null); // yalnızca düz satırı siler
        Assert.Equal(2, cart.Items.Count);
    }
}
