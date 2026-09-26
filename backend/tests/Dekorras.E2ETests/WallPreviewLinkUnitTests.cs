using Dekorras.Domain.WallCovering;
using Dekorras.Application.WallCovering;
using Dekorras.Storefront.WallCovering;

namespace Dekorras.E2ETests;

/// <summary>Sunucu gerektirmeyen "Duvarında Gör" birim testleri: deep link üretimi, açık yönlendirme
/// koruması ve özellik bayrağı (kapalıyken bağlantı hiç render edilmez; kısmi yüzde hedeflemeli).</summary>
public sealed class WallPreviewLinkUnitTests
{
    [Theory]
    [InlineData("/Product/Details?slug=a", "/Product/Details?slug=a")]
    [InlineData("/duvar-kagitlari", "/duvar-kagitlari")]
    [InlineData("//evil.com", null)]
    [InlineData("/\\evil.com", null)]
    [InlineData("https://evil.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    public void ReturnUrl_YalnizcaSiteIciGoreliYol(string input, string? expected)
    {
        Assert.Equal(expected, WallPreviewUrl.SafeReturnUrl(input));
    }

    [Fact]
    public void DeepLink_KonfigurasyonParametreleriniTasir()
    {
        var config = new WallConfiguration(400m, 200m, "textured", LengthUnit.M, FitMode.Crop, mirror: true, ImageFilter.Grayscale, new CropRect(0.1m, 0.05m, 0.8m, 0.9m));
        var url = WallPreviewUrl.Build("orman", config, null, "left", "//evil", "kart");

        Assert.StartsWith("/duvarinda-gor?product=orman", url);
        Assert.Contains("material=textured", url);
        Assert.Contains("unit=m", url);
        Assert.Contains("w_cm=400.0", url);
        Assert.Contains("h_cm=200.0", url);
        Assert.Contains("mirror=1", url);
        Assert.Contains("filter=grayscale", url);
        Assert.Contains("crop=0.1%2C0.05%2C0.8%2C0.9", url);
        Assert.Contains("align=left", url);
        Assert.Contains("src=kart", url);
        Assert.DoesNotContain("return=", url); // güvensiz return atıldı
    }

    [Fact]
    public void OzellikBayragi_SifirKapali_YuzKapaliDegil_KismiHedefleme()
    {
        Assert.Empty(WallFeatureDefinitionProvider.Build(WallFeatures.WallPreviewLink, 0).EnabledFor);
        Assert.Equal("AlwaysOn", WallFeatureDefinitionProvider.Build(WallFeatures.WallPreviewLink, 100).EnabledFor.Single().Name);

        var partial = WallFeatureDefinitionProvider.Build(WallFeatures.WallPreviewLink, 30).EnabledFor.Single();
        Assert.Equal("Microsoft.Targeting", partial.Name);
        Assert.Equal("30", partial.Parameters["Audience:DefaultRolloutPercentage"]);
    }
}
