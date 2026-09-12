using Dekorras.Application.Content.Helpers;
using Dekorras.Application.Content.Models;
using FluentAssertions;

namespace Dekorras.Application.Tests.Content;

public class BannerCssBuilderTests
{
    [Fact]
    public void BuildColumnClasses_YalnizcaXsVerilmisse_TekSinifDoner()
    {
        var settings = new BannerNodeSettings { ColXs = 12 };

        BannerCssBuilder.BuildColumnClasses(settings).Should().Be("col-12");
    }

    [Fact]
    public void BuildColumnClasses_TumBreakpointlerVeOffsetOrderVerilmisse_HepsiSiraylaEklenir()
    {
        var settings = new BannerNodeSettings
        {
            ColXs = 12,
            ColSm = 8,
            ColMd = 6,
            ColLg = 4,
            ColXl = 3,
            ColXxl = 2,
            Offset = 1,
            Order = 2
        };

        BannerCssBuilder.BuildColumnClasses(settings).Should().Be(
            "col-12 col-sm-8 col-md-6 col-lg-4 col-xl-3 col-xxl-2 offset-1 order-2");
    }

    [Fact]
    public void BuildColumnClasses_HiddenTrueIse_DNoneEklenir()
    {
        var settings = new BannerNodeSettings { ColXs = 12, Hidden = true };

        BannerCssBuilder.BuildColumnClasses(settings).Should().Contain("d-none");
    }

    [Fact]
    public void BuildColumnClasses_ColXsBelirtilmemisse_Varsayilan12Kullanilir()
    {
        var settings = new BannerNodeSettings();

        BannerCssBuilder.BuildColumnClasses(settings).Should().Be("col-12");
    }

    [Fact]
    public void BuildInlineStyle_BosAyarlarIcinBosStringDoner()
    {
        var settings = new BannerNodeSettings();

        BannerCssBuilder.BuildInlineStyle(settings).Should().BeEmpty();
    }

    [Fact]
    public void BuildInlineStyle_TumAlanlarVerilmisse_HepsiCssOlarakUretilir()
    {
        var settings = new BannerNodeSettings
        {
            BackgroundColor = "#ffffff",
            TextColor = "#111111",
            PaddingY = "40px",
            PaddingX = "20px",
            MinHeight = "380px",
            BorderRadius = "8px",
            TextAlign = "center"
        };

        var style = BannerCssBuilder.BuildInlineStyle(settings);

        style.Should().Contain("background-color:#ffffff;");
        style.Should().Contain("color:#111111;");
        style.Should().Contain("padding:40px 20px;");
        style.Should().Contain("min-height:380px;");
        style.Should().Contain("border-radius:8px;");
        style.Should().Contain("text-align:center;");
    }

    [Fact]
    public void BuildInlineStyle_ArkaPlanGorseliVerilmisse_CoverPositionIleUretilir()
    {
        var settings = new BannerNodeSettings { BackgroundImageUrl = "/uploads/banner-zones/x.jpg" };

        BannerCssBuilder.BuildInlineStyle(settings).Should().Be(
            "background-image:url('/uploads/banner-zones/x.jpg');background-size:cover;background-position:center;");
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip_AyniDegerleriUretir()
    {
        var settings = new BannerNodeSettings { ColXs = 6, ColMd = 4, BackgroundColor = "#000" };

        var json = BannerSettingsJson.Serialize(settings);
        var roundTripped = BannerSettingsJson.Deserialize<BannerNodeSettings>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.ColXs.Should().Be(6);
        roundTripped.ColMd.Should().Be(4);
        roundTripped.BackgroundColor.Should().Be("#000");
    }

    [Fact]
    public void Deserialize_NullVeyaBosJson_VarsayilanNesneDoner()
    {
        BannerSettingsJson.Deserialize<BannerNodeSettings>(null).Should().NotBeNull();
        BannerSettingsJson.Deserialize<BannerNodeSettings>("").Should().NotBeNull();
    }

    [Fact]
    public void Deserialize_GecersizJson_VarsayilanNesneDoner()
    {
        var result = BannerSettingsJson.Deserialize<BannerNodeSettings>("{not-json");

        result.Should().NotBeNull();
        result!.ColXs.Should().BeNull();
    }

    [Fact]
    public void BuildImageOverlayStyle_BosAyarlarIcinBosStringDoner()
    {
        var settings = new BannerContentSettings();

        BannerCssBuilder.BuildImageOverlayStyle(settings).Should().BeEmpty();
    }

    [Fact]
    public void BuildImageOverlayStyle_YalnizcaRenkVerilmisse_YalnizcaColorUretilir()
    {
        var settings = new BannerContentSettings { AltTextColor = "#ffffff" };

        BannerCssBuilder.BuildImageOverlayStyle(settings).Should().Be("color:#ffffff;");
    }

    [Theory]
    [InlineData("left", "flex-start")]
    [InlineData("center", "center")]
    [InlineData("right", "flex-end")]
    public void BuildImageOverlayStyle_HizalamaVerilmisse_JustifyContentVeTextAlignUretilir(string align, string expectedJustify)
    {
        var settings = new BannerContentSettings { AltTextAlign = align };

        BannerCssBuilder.BuildImageOverlayStyle(settings).Should().Be($"justify-content:{expectedJustify};text-align:{align};");
    }

    [Fact]
    public void BuildImageOverlayStyle_RenkVeHizalamaBirlikte_HerIkisiDeUretilir()
    {
        var settings = new BannerContentSettings { AltTextColor = "#111", AltTextAlign = "center" };

        var style = BannerCssBuilder.BuildImageOverlayStyle(settings);

        style.Should().Contain("color:#111;");
        style.Should().Contain("justify-content:center;text-align:center;");
    }
}
