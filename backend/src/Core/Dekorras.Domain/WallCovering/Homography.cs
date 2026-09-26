namespace Dekorras.Domain.WallCovering;

public readonly record struct Point2(double X, double Y);

/// <summary>Duvar dörtgeni: sol üst, sağ üst, sağ alt, sol alt köşe (piksel, sahne görselinde).</summary>
public sealed record WallQuad(Point2 TopLeft, Point2 TopRight, Point2 BottomRight, Point2 BottomLeft)
{
    public Point2[] Corners => [TopLeft, TopRight, BottomRight, BottomLeft];

    /// <summary>Üst ve alt kenarın ortalama uzunluğu - cm→piksel ölçeği için (spec 1.6.3).</summary>
    public double AverageWidthPx =>
        (Distance(TopLeft, TopRight) + Distance(BottomLeft, BottomRight)) / 2d;

    public double AverageHeightPx =>
        (Distance(TopLeft, BottomLeft) + Distance(TopRight, BottomRight)) / 2d;

    /// <summary>Duvarın [u0,u1]×[v0,v1] (0–1, duvar düzleminde) alt bölgesinin sahnedeki dörtgeni.
    /// Kullanıcı ölçüsü duvardan küçükse poster bu alt bölgeye yerleşir.</summary>
    public WallQuad SubQuad(double u0, double v0, double u1, double v1)
    {
        var h = Homography.FromUnitSquare(this);
        return new WallQuad(h.Map(u0, v0), h.Map(u1, v0), h.Map(u1, v1), h.Map(u0, v1));
    }

    public bool IsConvex()
    {
        var c = Corners;
        var sign = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = c[i];
            var b = c[(i + 1) % 4];
            var d = c[(i + 2) % 4];
            var cross = (b.X - a.X) * (d.Y - b.Y) - (b.Y - a.Y) * (d.X - b.X);
            var s = Math.Sign(cross);
            if (s == 0) return false;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }

    private static double Distance(Point2 a, Point2 b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
}

/// <summary>3x3 perspektif (homografi) dönüşümü. İstemcideki wall-visualizer.js ile BİREBİR aynı
/// formül kullanılır (birim kareden dörtgene, Heckbert'in kapalı form çözümü) - sunucu ve istemci
/// render'ı aynı köşe eşlemesini üretsin diye.</summary>
public sealed class Homography
{
    // Satır öncelikli: [a b c; d e f; g h 1]
    private readonly double[] _m;

    private Homography(double[] m) => _m = m;

    public IReadOnlyList<double> Matrix => _m;

    /// <summary>(0,0)→TL, (1,0)→TR, (1,1)→BR, (0,1)→BL eşlemesi.</summary>
    public static Homography FromUnitSquare(WallQuad quad)
    {
        var (x0, y0) = (quad.TopLeft.X, quad.TopLeft.Y);
        var (x1, y1) = (quad.TopRight.X, quad.TopRight.Y);
        var (x2, y2) = (quad.BottomRight.X, quad.BottomRight.Y);
        var (x3, y3) = (quad.BottomLeft.X, quad.BottomLeft.Y);

        var dx1 = x1 - x2;
        var dx2 = x3 - x2;
        var dy1 = y1 - y2;
        var dy2 = y3 - y2;
        var sx = x0 - x1 + x2 - x3;
        var sy = y0 - y1 + y2 - y3;

        double g, h;
        if (Math.Abs(sx) < 1e-12 && Math.Abs(sy) < 1e-12)
        {
            g = 0;
            h = 0;
        }
        else
        {
            var den = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(den) < 1e-12) throw new ArgumentException("Dörtgen dejenere (köşeler aynı doğru üzerinde).");
            g = (sx * dy2 - dx2 * sy) / den;
            h = (dx1 * sy - sx * dy1) / den;
        }

        var a = x1 - x0 + g * x1;
        var b = x3 - x0 + h * x3;
        var c = x0;
        var d = y1 - y0 + g * y1;
        var e = y3 - y0 + h * y3;
        var f = y0;

        return new Homography([a, b, c, d, e, f, g, h, 1]);
    }

    /// <summary>Genişliği w, yüksekliği h olan kaynak dikdörtgenden (piksel) dörtgene.</summary>
    public static Homography FromRect(double width, double height, WallQuad quad)
    {
        var unit = FromUnitSquare(quad);
        var scale = new Homography([1 / width, 0, 0, 0, 1 / height, 0, 0, 0, 1]);
        return unit.Multiply(scale);
    }

    public Point2 Map(double x, double y)
    {
        var m = _m;
        var w = m[6] * x + m[7] * y + m[8];
        return new Point2((m[0] * x + m[1] * y + m[2]) / w, (m[3] * x + m[4] * y + m[5]) / w);
    }

    public Homography Multiply(Homography other)
    {
        var a = _m;
        var b = other._m;
        var r = new double[9];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return new Homography(r);
    }

    public Homography Invert()
    {
        var m = _m;
        var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(det) < 1e-15) throw new InvalidOperationException("Homografi tersinir değil.");
        var inv = new[]
        {
            (m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
            (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
            (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det,
        };
        return new Homography(inv);
    }
}
