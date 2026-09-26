using Dekorras.Domain.Common;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Domain.Tests.WallCovering;

public class WallGeometryTests
{
    private static readonly WallQuad Quad = new(new Point2(120, 80), new Point2(900, 110), new Point2(880, 620), new Point2(140, 650));

    private static void AssertClose(Point2 expected, Point2 actual) =>
        Assert.True(Math.Abs(expected.X - actual.X) < 1e-6 && Math.Abs(expected.Y - actual.Y) < 1e-6,
            $"Beklenen ({expected.X}, {expected.Y}), gelen ({actual.X}, {actual.Y})");

    [Fact]
    public void Homografi_BirimKareKoseleriDortgenKoselerineEslenir()
    {
        var h = Homography.FromUnitSquare(Quad);

        AssertClose(Quad.TopLeft, h.Map(0, 0));
        AssertClose(Quad.TopRight, h.Map(1, 0));
        AssertClose(Quad.BottomRight, h.Map(1, 1));
        AssertClose(Quad.BottomLeft, h.Map(0, 1));
    }

    [Fact]
    public void Homografi_KaynakDikdortgenPikselleriEslenir_TersiGeriDondurur()
    {
        var h = Homography.FromRect(2000, 1000, Quad);

        AssertClose(Quad.TopLeft, h.Map(0, 0));
        AssertClose(Quad.BottomRight, h.Map(2000, 1000));

        var inverse = h.Invert();
        var mid = h.Map(700, 300);
        AssertClose(new Point2(700, 300), inverse.Map(mid.X, mid.Y));
    }

    [Fact]
    public void Homografi_ParalelKenarDortgendeAfinOlur()
    {
        var rect = new WallQuad(new Point2(0, 0), new Point2(100, 0), new Point2(100, 50), new Point2(0, 50));
        var h = Homography.FromUnitSquare(rect);

        AssertClose(new Point2(50, 25), h.Map(0.5, 0.5));
        Assert.Equal(0d, h.Matrix[6], 9);
        Assert.Equal(0d, h.Matrix[7], 9);
    }

    [Fact]
    public void DisbukeyOlmayanDortgen_SahneTarafindanReddedilir()
    {
        var bowTie = new WallQuad(new Point2(0, 0), new Point2(100, 100), new Point2(100, 0), new Point2(0, 100));
        Assert.False(bowTie.IsConvex());
        Assert.Throws<DomainException>(() =>
            new RoomScene("x", RoomType.Other, "/x.jpg", 200, 200, bowTie, 300m, 250m));
    }

    [Fact]
    public void CmPikselOlcegi_DuvarGenisligindenHesaplanir()
    {
        var rect = new WallQuad(new Point2(0, 0), new Point2(800, 0), new Point2(800, 500), new Point2(0, 500));
        Assert.Equal(2d, WallLayout.PixelsPerCm(rect, 400), 9);
    }

    [Fact]
    public void KucukPoster_OrtayaYerlesir_KalanDuvarGorunur()
    {
        var p = WallLayout.Place(400, 250, 300, 200, WallAlign.Center);

        Assert.Equal(0.125, p.U0, 9);
        Assert.Equal(0.875, p.U1, 9);
        Assert.Equal(0.1, p.V0, 9);
        Assert.Equal(0.9, p.V1, 9);
        Assert.False(p.IsClipped);
    }

    [Fact]
    public void Hiza_SolVeSag()
    {
        Assert.Equal(0d, WallLayout.Place(400, 250, 200, 250, WallAlign.Left).U0, 9);
        Assert.Equal(1d, WallLayout.Place(400, 250, 200, 250, WallAlign.Right).U1, 9);
    }

    [Fact]
    public void BuyukPoster_DuvaraKirpilir_UyariIsaretlenir()
    {
        var p = WallLayout.Place(400, 250, 500, 200, WallAlign.Center);

        Assert.True(p.ClippedHorizontally);
        Assert.False(p.ClippedVertically);
        Assert.Equal(0d, p.U0, 9);
        Assert.Equal(1d, p.U1, 9);
    }

    [Fact]
    public void DesenDoseme_DuzTekrar()
    {
        var tiles = WallLayout.TilePattern(100, 100, 50, 50, RepeatType.Straight);
        Assert.Equal(4, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.YCm >= 0));
    }

    [Fact]
    public void DesenDoseme_YarimKaydirma_TekSutunlarYarimTekrarKayar()
    {
        var tiles = WallLayout.TilePattern(100, 100, 50, 50, RepeatType.HalfDrop);

        Assert.Equal(2, tiles.Count(t => t.XCm == 0));
        Assert.Equal(3, tiles.Count(t => t.XCm == 50)); // -25, 25, 75
        Assert.Contains(tiles, t => t.XCm == 50 && t.YCm == -25);
    }

    [Fact]
    public void EfektifDpi_KirpilanPikselVeHedefOlcudenHesaplanir()
    {
        // 100 cm = 39,37 inç; 2362 px → ~60 DPI
        var dpi = WallLayout.EffectiveDpi(2362, 2362, 100m, 100m);
        Assert.InRange(dpi, 59.9, 60.1);
    }

    [Fact]
    public void AltDortgen_TamDuvarIcinAyniKoseleriVerir()
    {
        var sub = Quad.SubQuad(0, 0, 1, 1);
        AssertClose(Quad.TopLeft, sub.TopLeft);
        AssertClose(Quad.BottomRight, sub.BottomRight);
    }
}
