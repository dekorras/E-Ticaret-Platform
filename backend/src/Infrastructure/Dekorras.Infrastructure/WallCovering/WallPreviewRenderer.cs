using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Dekorras.Infrastructure.WallCovering;

/// <summary>ImageSharp ile sunucu render'ı. Katman sırası istemciyle (wall-visualizer.js) birebir aynıdır:
/// 1) taban görsel 2) poster → duvar dörtgenine perspektif (ters homografi, bilineer örnekleme, kenarlarda
/// 2×2 süper örnekleme) 3) gölge haritası ile multiply (yalnızca poster pikselleri) 4) ön plan maskesi
/// 5) (istenirse) filigran.</summary>
public sealed class WallPreviewRenderer(IWallImageStore store) : IWallPreviewRenderer
{
    private const int MaxPosterEdgePx = 6000;

    public async Task<byte[]> RenderAsync(WallRenderRequest request, CancellationToken cancellationToken)
    {
        var scene = request.Scene;
        using var canvas = await LoadAsync<Rgba32>(scene.BaseImageUrl, cancellationToken)
            ?? throw new FileNotFoundException("Sahne görseli bulunamadı.", scene.BaseImageUrl);
        using var poster = await LoadPosterAsync(request.Poster, cancellationToken);

        var cfg = request.Configuration;
        var wallW = (double)scene.RealWallWidthCm;
        var wallH = (double)scene.RealWallHeightCm;
        var posterW = (double)cfg.WidthCm;
        var posterH = (double)cfg.HeightCm;

        var placement = WallLayout.Place(wallW, wallH, posterW, posterH, request.Align);

        // Sahne dosyasının gerçek boyutu kayıttakinden farklıysa köşeler ölçeklenir.
        var sx = canvas.Width / (double)scene.ImageWidthPx;
        var sy = canvas.Height / (double)scene.ImageHeightPx;
        var q = scene.WallQuad;
        var quad = new WallQuad(Scale(q.TopLeft), Scale(q.TopRight), Scale(q.BottomRight), Scale(q.BottomLeft))
            .SubQuad(placement.U0, placement.V0, placement.U1, placement.V1);

        // Posterin duvarda görünen penceresi (cm) - poster duvardan büyükse duvar ölçüsüne kırpılır.
        var visibleW = Math.Min(posterW, wallW);
        var visibleH = Math.Min(posterH, wallH);
        var offsetX = placement.ClippedHorizontally
            ? request.Align switch { WallAlign.Left => 0, WallAlign.Right => posterW - wallW, _ => (posterW - wallW) / 2 }
            : 0;
        var offsetY = placement.ClippedVertically ? (posterH - wallH) / 2 : 0;

        var textureW = Math.Clamp((int)Math.Ceiling(quad.AverageWidthPx * 1.25), 32, 3000);
        var pxPerCm = textureW / visibleW;
        pxPerCm = Math.Min(pxPerCm, MaxPosterEdgePx / Math.Max(posterW, posterH));
        textureW = Math.Max(1, (int)Math.Round(visibleW * pxPerCm));
        var textureH = Math.Max(1, (int)Math.Round(visibleH * pxPerCm));

        using var texture = BuildPosterTexture(poster, request, pxPerCm, offsetX, offsetY, textureW, textureH);

        byte[]? shadow = null;
        if (scene.ShadowMapUrl is not null)
        {
            using var shadowImage = await LoadAsync<L8>(scene.ShadowMapUrl, cancellationToken);
            if (shadowImage is not null)
            {
                if (shadowImage.Width != canvas.Width || shadowImage.Height != canvas.Height)
                    shadowImage.Mutate(x => x.Resize(canvas.Width, canvas.Height));
                shadow = new byte[canvas.Width * canvas.Height];
                shadowImage.CopyPixelDataTo(shadow);
            }
        }

        Warp(canvas, texture, quad, shadow);

        if (scene.ForegroundMaskUrl is not null)
        {
            using var mask = await LoadAsync<Rgba32>(scene.ForegroundMaskUrl, cancellationToken);
            if (mask is not null)
            {
                if (mask.Width != canvas.Width || mask.Height != canvas.Height)
                    mask.Mutate(x => x.Resize(canvas.Width, canvas.Height));
                canvas.Mutate(x => x.DrawImage(mask, 1f));
            }
        }

        if (request.OutputWidthPx > 0 && request.OutputWidthPx < canvas.Width)
            canvas.Mutate(x => x.Resize(request.OutputWidthPx, 0, KnownResamplers.Lanczos3));

        if (request.Watermark) ImageDerivativeService.Watermark(canvas);

        using var ms = new MemoryStream();
        await canvas.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 86 }, cancellationToken);
        return ms.ToArray();

        Point2 Scale(Point2 p) => new(p.X * sx, p.Y * sy);
    }

    /// <summary>Posteri müşterinin ölçüsünde (cm × pxPerCm) üretir, ardından duvarda görünen pencereyi keser.</summary>
    public static Image<Rgba32> BuildPosterTexture(Image<Rgba32> source, WallRenderRequest request, double pxPerCm, double offsetXcm, double offsetYcm, int textureW, int textureH)
    {
        var cfg = request.Configuration;
        var fullW = Math.Max(1, (int)Math.Round((double)cfg.WidthCm * pxPerCm));
        var fullH = Math.Max(1, (int)Math.Round((double)cfg.HeightCm * pxPerCm));
        Image<Rgba32> full;

        if (request.Poster.ProductType == WallProductType.Pattern && request.Poster.RepeatWidthCm is > 0 && request.Poster.RepeatHeightCm is > 0)
        {
            full = new Image<Rgba32>(fullW, fullH, Color.White);
            var tileW = Math.Max(1, (int)Math.Round((double)request.Poster.RepeatWidthCm.Value * pxPerCm));
            var tileH = Math.Max(1, (int)Math.Round((double)request.Poster.RepeatHeightCm.Value * pxPerCm));
            using var tile = source.Clone(x => x.Resize(tileW, tileH));
            foreach (var t in WallLayout.TilePattern((double)cfg.WidthCm, (double)cfg.HeightCm, (double)request.Poster.RepeatWidthCm.Value, (double)request.Poster.RepeatHeightCm.Value, request.Poster.RepeatType))
            {
                var at = new Point((int)Math.Round(t.XCm * pxPerCm), (int)Math.Round(t.YCm * pxPerCm));
                full.Mutate(x => x.DrawImage(tile, at, 1f));
            }
        }
        else if (cfg.Fit == FitMode.Stretch)
        {
            full = source.Clone(x => x.Resize(fullW, fullH));
        }
        else
        {
            var crop = cfg.Crop ?? DefaultCrop(source.Width, source.Height, (double)cfg.WidthCm / (double)cfg.HeightCm);
            var (cx, cy, cw, ch) = crop.ToPixels(source.Width, source.Height);
            var rect = Rectangle.Intersect(new Rectangle(cx, cy, cw, ch), new Rectangle(0, 0, source.Width, source.Height));
            if (rect.Width <= 0 || rect.Height <= 0) rect = new Rectangle(0, 0, source.Width, source.Height);
            full = source.Clone(x => x.Crop(rect).Resize(fullW, fullH));
        }

        full.Mutate(x =>
        {
            if (cfg.Mirror) x.Flip(FlipMode.Horizontal);
            if (cfg.Filter == ImageFilter.Grayscale) x.Grayscale();
            else if (cfg.Filter == ImageFilter.Sepia) x.Sepia();
        });

        ApplyMaterialTexture(full, cfg.MaterialCode);
        if (request.PanelLines) DrawPanelLines(full, (double)request.PanelWidthCm, (double)request.BleedCm, pxPerCm, (double)cfg.WidthCm);

        var window = Rectangle.Intersect(
            new Rectangle((int)Math.Round(offsetXcm * pxPerCm), (int)Math.Round(offsetYcm * pxPerCm), textureW, textureH),
            new Rectangle(0, 0, full.Width, full.Height));
        if (window.Width == full.Width && window.Height == full.Height) return full;

        var result = full.Clone(x => x.Crop(window));
        full.Dispose();
        return result;
    }

    /// <summary>Hedef orana uyan, görsele sığan en büyük ortalanmış kırpma alanı (istemciyle aynı kural).</summary>
    public static CropRect DefaultCrop(int imageW, int imageH, double targetRatio)
    {
        var ir = imageW / (double)imageH;
        return targetRatio > ir
            ? new CropRect(0m, (decimal)((1 - ir / targetRatio) / 2), 1m, (decimal)(ir / targetRatio))
            : new CropRect((decimal)((1 - targetRatio / ir) / 2), 0m, (decimal)(targetRatio / ir), 1m);
    }

    /// <summary>Dokulu malzemelerde hafif doku bindirmesi (yalnızca önizleme - baskı dosyasına uygulanmaz).</summary>
    private static void ApplyMaterialTexture(Image<Rgba32> image, string materialCode)
    {
        var (strength, horizontalLines) = materialCode switch
        {
            "textured" => (10, false),
            "textile" or "premium-textile" or "canvas-adhesive" => (8, false),
            "straw" => (14, true),
            "metallic" => (6, false),
            _ => (0, false)
        };
        if (strength == 0) return;

        var random = new Random(42); // deterministik: aynı istek → aynı piksel (önbellek/test tutarlılığı)
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var lineShade = horizontalLines && y % 3 == 0 ? -strength : 0;
                for (var x = 0; x < row.Length; x++)
                {
                    var delta = lineShade + random.Next(-strength, strength + 1) / 2;
                    ref var p = ref row[x];
                    p = new Rgba32((byte)Math.Clamp(p.R + delta, 0, 255), (byte)Math.Clamp(p.G + delta, 0, 255), (byte)Math.Clamp(p.B + delta, 0, 255), p.A);
                }
            }
        });
    }

    /// <summary>Panel ek yerleri: k × panel eni − pay/2 (pay iki kenara eşit dağıtılır - istemciyle aynı VARSAYIM).</summary>
    private static void DrawPanelLines(Image<Rgba32> image, double panelWidthCm, double bleedCm, double pxPerCm, double widthCm)
    {
        if (panelWidthCm <= 0) return;
        var thickness = Math.Max(1f, image.Width / 700f);
        var light = new SolidPen(new PenOptions(Color.White.WithAlpha(0.9f), thickness, [4f, 3f]));
        var dark = new SolidPen(new PenOptions(Color.Black.WithAlpha(0.55f), thickness, [4f, 3f]));
        image.Mutate(ctx =>
        {
            for (var xCm = panelWidthCm - bleedCm / 2; xCm < widthCm; xCm += panelWidthCm)
            {
                var x = (float)(xCm * pxPerCm);
                ctx.DrawLine(light, new PointF(x, 0), new PointF(x, image.Height));
                ctx.DrawLine(dark, new PointF(x + thickness, 0), new PointF(x + thickness, image.Height));
            }
        });
    }

    /// <summary>Ters homografi ile hedef dörtgenin sınırlayıcı kutusundaki her pikseli poster dokusuna
    /// eşler. İç piksellerde tek örnek, kenar piksellerinde 2×2 süper örnekleme (kenar yumuşatma).</summary>
    public static void Warp(Image<Rgba32> canvas, Image<Rgba32> texture, WallQuad quad, byte[]? shadow)
    {
        var tw = texture.Width;
        var th = texture.Height;
        var tex = new Rgba32[tw * th];
        texture.CopyPixelDataTo(tex);

        var inverse = Homography.FromRect(tw, th, quad).Invert();
        var corners = quad.Corners;
        var minX = Math.Max(0, (int)Math.Floor(corners.Min(c => c.X)));
        var maxX = Math.Min(canvas.Width - 1, (int)Math.Ceiling(corners.Max(c => c.X)));
        var minY = Math.Max(0, (int)Math.Floor(corners.Min(c => c.Y)));
        var maxY = Math.Min(canvas.Height - 1, (int)Math.Ceiling(corners.Max(c => c.Y)));
        var canvasWidth = canvas.Width;

        canvas.ProcessPixelRows(accessor =>
        {
            Span<(double X, double Y)> offsets = [(0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75)];
            for (var y = minY; y <= maxY; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = minX; x <= maxX; x++)
                {
                    double r = 0, g = 0, b = 0;
                    var hits = 0;
                    var center = inverse.Map(x + 0.5, y + 0.5);
                    var centerInside = Inside(center.X, center.Y, tw, th);

                    // Hızlı yol: merkez dokunun en az 1 px içindeyse tek örnek yeterli.
                    if (centerInside && center.X > 1 && center.Y > 1 && center.X < tw - 1 && center.Y < th - 1)
                    {
                        Sample(tex, tw, th, center.X, center.Y, ref r, ref g, ref b);
                        hits = 4;
                        r *= 4; g *= 4; b *= 4;
                    }
                    else
                    {
                        foreach (var (ox, oy) in offsets)
                        {
                            var s = inverse.Map(x + ox, y + oy);
                            if (!Inside(s.X, s.Y, tw, th)) continue;
                            Sample(tex, tw, th, s.X, s.Y, ref r, ref g, ref b);
                            hits++;
                        }
                    }
                    if (hits == 0) continue;

                    r /= hits; g /= hits; b /= hits;
                    if (shadow is not null)
                    {
                        var k = shadow[y * canvasWidth + x] / 255d;
                        r *= k; g *= k; b *= k;
                    }

                    var coverage = hits / 4d;
                    ref var dst = ref row[x];
                    dst = new Rgba32(
                        (byte)Math.Clamp(dst.R + (r - dst.R) * coverage, 0, 255),
                        (byte)Math.Clamp(dst.G + (g - dst.G) * coverage, 0, 255),
                        (byte)Math.Clamp(dst.B + (b - dst.B) * coverage, 0, 255),
                        255);
                }
            }
        });
    }

    private static bool Inside(double u, double v, int w, int h) => u >= 0 && v >= 0 && u < w && v < h;

    private static void Sample(Rgba32[] tex, int w, int h, double u, double v, ref double r, ref double g, ref double b)
    {
        var fx = Math.Clamp(u - 0.5, 0, w - 1);
        var fy = Math.Clamp(v - 0.5, 0, h - 1);
        var x0 = (int)fx;
        var y0 = (int)fy;
        var x1 = Math.Min(x0 + 1, w - 1);
        var y1 = Math.Min(y0 + 1, h - 1);
        var ax = fx - x0;
        var ay = fy - y0;
        var p00 = tex[y0 * w + x0];
        var p10 = tex[y0 * w + x1];
        var p01 = tex[y1 * w + x0];
        var p11 = tex[y1 * w + x1];
        r += Lerp(Lerp(p00.R, p10.R, ax), Lerp(p01.R, p11.R, ax), ay);
        g += Lerp(Lerp(p00.G, p10.G, ax), Lerp(p01.G, p11.G, ax), ay);
        b += Lerp(Lerp(p00.B, p10.B, ax), Lerp(p01.B, p11.B, ax), ay);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private async Task<Image<TPixel>?> LoadAsync<TPixel>(string url, CancellationToken cancellationToken) where TPixel : unmanaged, IPixel<TPixel>
    {
        await using var stream = store.OpenPublic(url);
        return stream is null ? null : await Image.LoadAsync<TPixel>(stream, cancellationToken);
    }

    private async Task<Image<Rgba32>> LoadPosterAsync(PosterSource source, CancellationToken cancellationToken)
    {
        var stream = (source.PrivateKey is not null ? store.OpenPrivate(source.PrivateKey) : null)
                     ?? (source.PublicUrl is not null ? store.OpenPublic(source.PublicUrl) : null)
                     ?? throw new FileNotFoundException("Poster görseli bulunamadı.");
        await using (stream)
        {
            var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
            image.Mutate(x => x.AutoOrient());
            return image;
        }
    }
}
