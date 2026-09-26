namespace Dekorras.Domain.WallCovering;

/// <summary>Posterin sahne duvarı üzerindeki yeri, duvar düzleminde normalize (0–1) koordinatlarda.</summary>
public sealed record WallPlacement(double U0, double V0, double U1, double V1, bool ClippedHorizontally, bool ClippedVertically)
{
    public bool IsClipped => ClippedHorizontally || ClippedVertically;
}

public sealed record PatternTile(double XCm, double YCm, double WidthCm, double HeightCm);

/// <summary>Duvar görüntüleyicinin (spec 1.6.3) ölçek ve yerleşim hesapları - istemci JS aynı kuralları uygular.</summary>
public static class WallLayout
{
    /// <summary>Kullanıcı ölçüsü sahne duvarından küçükse poster seçilen hizaya (yatayda) ve dikeyde
    /// ortaya yerleşir; büyükse duvar ölçüsüne kırpılır (Clipped=true, arayüz uyarı gösterir).
    /// VARSAYIM: dikey hizalama her zaman orta - spec yalnızca yatay hizayı (sol/orta/sağ) tanımlıyor.</summary>
    public static WallPlacement Place(double wallWidthCm, double wallHeightCm, double posterWidthCm, double posterHeightCm, WallAlign align)
    {
        if (wallWidthCm <= 0 || wallHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(wallWidthCm), "Duvar ölçüsü sıfırdan büyük olmalı.");

        var clipH = posterWidthCm > wallWidthCm;
        var clipV = posterHeightCm > wallHeightCm;
        var w = Math.Min(posterWidthCm, wallWidthCm) / wallWidthCm;
        var h = Math.Min(posterHeightCm, wallHeightCm) / wallHeightCm;

        var u0 = align switch
        {
            WallAlign.Left => 0d,
            WallAlign.Right => 1d - w,
            _ => (1d - w) / 2d
        };
        var v0 = (1d - h) / 2d;

        return new WallPlacement(u0, v0, u0 + w, v0 + h, clipH, clipV);
    }

    public static double PixelsPerCm(WallQuad quad, double realWallWidthCm) => quad.AverageWidthPx / realWallWidthCm;

    /// <summary>Desen (Pattern) ürünün verilen alana döşenmesi. HalfDrop'ta her tek sütun yarım
    /// tekrar boyu yukarı kaydırılır; alanı tamamen kaplamak için üstten bir karo fazladan başlar.</summary>
    public static IReadOnlyList<PatternTile> TilePattern(double areaWidthCm, double areaHeightCm, double repeatWidthCm, double repeatHeightCm, RepeatType repeatType)
    {
        if (repeatWidthCm <= 0 || repeatHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(repeatWidthCm), "Tekrar ölçüsü sıfırdan büyük olmalı.");

        var tiles = new List<PatternTile>();
        var columns = (int)Math.Ceiling(areaWidthCm / repeatWidthCm);

        for (var col = 0; col < columns; col++)
        {
            var offsetY = repeatType == RepeatType.HalfDrop && col % 2 == 1 ? -repeatHeightCm / 2d : 0d;
            for (var y = offsetY; y < areaHeightCm; y += repeatHeightCm)
                tiles.Add(new PatternTile(col * repeatWidthCm, y, repeatWidthCm, repeatHeightCm));
        }

        return tiles;
    }

    /// <summary>Kırpılan görsel alanının hedef baskı ölçüsündeki efektif çözünürlüğü (DPI). Eşik
    /// altındaysa arayüz "Baskı kalitesi düşebilir" uyarısı verir (spec 1.5 - adım 3).</summary>
    public static double EffectiveDpi(int croppedWidthPx, int croppedHeightPx, decimal targetWidthCm, decimal targetHeightCm)
    {
        var dpiX = croppedWidthPx / ((double)targetWidthCm / 2.54d);
        var dpiY = croppedHeightPx / ((double)targetHeightCm / 2.54d);
        return Math.Min(dpiX, dpiY);
    }
}
