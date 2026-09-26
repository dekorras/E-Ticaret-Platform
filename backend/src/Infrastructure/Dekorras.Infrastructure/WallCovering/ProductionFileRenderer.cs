using System.Globalization;
using System.Text;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Dekorras.Infrastructure.WallCovering;

/// <summary>Üretim dosyası ve onay önizlemesi. Tüm poster TEK SEFERDE büyük çözünürlükte üretilmez (2000 cm
/// × 150 DPI ≈ 118 bin piksel genişlik): her panel, kaynaktaki kendi penceresinden doğrudan üretilir.
/// Üretim alanı = müşteri ölçüsü + kesim payı; kırpma alanı pay kadar orantılı genişletilir (merkez korunur).
/// VARSAYIM: panel başına en fazla 60 megapiksel - aşılırsa DPI orantılı düşürülür ve etikete yazılır.</summary>
public sealed class ProductionFileRenderer(IWallImageStore store) : IProductionFileRenderer
{
    private const double MarginCm = 1.5;
    private const long MaxPanelPixels = 60_000_000;
    private const double PtPerCm = 72 / 2.54;

    public async Task<ProductionPdf> RenderPanelsPdfAsync(ProductionRenderRequest request, CancellationToken cancellationToken)
    {
        using var source = await LoadAsync(request.Poster, cancellationToken);
        var cfg = request.Configuration;
        var prodW = (double)(cfg.WidthCm + request.BleedCm);
        var prodH = (double)(cfg.HeightCm + request.BleedCm);
        var panelW = (double)request.PanelWidthCm;
        var panelCount = (int)Math.Ceiling(prodW / panelW);

        var dpi = (double)request.TargetDpi;
        var maxPanelPx = panelW / 2.54 * dpi * (prodH / 2.54 * dpi);
        if (maxPanelPx > MaxPanelPixels) dpi *= Math.Sqrt(MaxPanelPixels / maxPanelPx);
        var pxPerCm = dpi / 2.54;

        var pages = new List<(byte[] Jpeg, int W, int H, double WidthPt, double HeightPt)>();
        for (var i = 0; i < panelCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x0 = i * panelW;
            var width = Math.Min(panelW, prodW - x0);
            using var panel = RenderWindow(source, request, prodW, prodH, x0, 0, width, prodH, pxPerCm);
            using var page = AddMarginsAndMarks(panel, pxPerCm, i + 1, panelCount, request, width, prodH, (int)Math.Round(dpi));
            using var ms = new MemoryStream();
            await page.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 92 }, cancellationToken);
            pages.Add((ms.ToArray(), page.Width, page.Height, (width + 2 * MarginCm) * PtPerCm, (prodH + 2 * MarginCm) * PtPerCm));
        }

        return new ProductionPdf(MinimalPdf.Build(pages, $"{request.OrderNumber} - {request.ItemLabel}"), panelCount, (int)Math.Round(dpi));
    }

    public async Task<byte[]> RenderProofPreviewAsync(ProductionRenderRequest request, int widthPx, CancellationToken cancellationToken)
    {
        using var source = await LoadAsync(request.Poster, cancellationToken);
        var cfg = request.Configuration;
        var bleed = (double)request.BleedCm;
        var prodW = (double)cfg.WidthCm + bleed;
        var prodH = (double)cfg.HeightCm + bleed;
        var pxPerCm = widthPx / prodW;

        using var image = RenderWindow(source, request, prodW, prodH, 0, 0, prodW, prodH, pxPerCm);
        var family = Fonts();
        image.Mutate(ctx =>
        {
            // Kesim payı alanı (her kenarda pay/2) yarı saydam gösterilir; iç çizgi = müşterinin ölçüsü.
            var b = (float)(bleed / 2 * pxPerCm);
            var shade = Color.White.WithAlpha(0.55f);
            ctx.Fill(shade, new RectangularPolygon(0, 0, image.Width, b));
            ctx.Fill(shade, new RectangularPolygon(0, image.Height - b, image.Width, b));
            ctx.Fill(shade, new RectangularPolygon(0, b, b, image.Height - 2 * b));
            ctx.Fill(shade, new RectangularPolygon(image.Width - b, b, b, image.Height - 2 * b));
            ctx.Draw(new SolidPen(new PenOptions(Color.Red, 2f, [6f, 4f])), new RectangularPolygon(b, b, image.Width - 2 * b, image.Height - 2 * b));

            var panelW = (double)request.PanelWidthCm;
            var n = 1;
            for (var x = panelW; x < prodW; x += panelW, n++)
            {
                var px = (float)(x * pxPerCm);
                ctx.DrawLine(new SolidPen(new PenOptions(Color.Black.WithAlpha(0.7f), 2f, [5f, 4f])), new PointF(px, 0), new PointF(px, image.Height));
            }
            if (family is { } f)
            {
                var font = f.CreateFont(Math.Max(12, image.Width / 60f), FontStyle.Bold);
                for (var i = 0; i < n; i++)
                    ctx.DrawText($"{i + 1}", font, Color.Black, new PointF((float)((i * panelW + 1) * pxPerCm), (float)(bleed / 2 * pxPerCm + 4)));
                ctx.DrawText("ONAY ÖNİZLEMESİ", f.CreateFont(Math.Max(18, image.Width / 18f), FontStyle.Bold), Color.White.WithAlpha(0.35f),
                    new PointF(image.Width * 0.12f, image.Height * 0.42f));
            }
        });

        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 86 }, cancellationToken);
        return ms.ToArray();
    }

    /// <summary>Üretim alanının (prodW × prodH cm) [x0, x0+w] × [y0, y0+h] penceresini pxPerCm çözünürlükte üretir.</summary>
    public static Image<Rgba32> RenderWindow(Image<Rgba32> source, ProductionRenderRequest request, double prodW, double prodH,
        double x0, double y0, double w, double h, double pxPerCm)
    {
        var cfg = request.Configuration;
        var outW = Math.Max(1, (int)Math.Round(w * pxPerCm));
        var outH = Math.Max(1, (int)Math.Round(h * pxPerCm));
        var result = new Image<Rgba32>(outW, outH, Color.White);

        // Ayna: üretim alanı yatayda çevrilir → pencere, çevrilmiş koordinatlardan alınıp sonra çevrilir.
        var wx0 = cfg.Mirror ? prodW - (x0 + w) : x0;

        if (request.Poster.ProductType == WallProductType.Pattern && request.Poster.RepeatWidthCm is > 0 && request.Poster.RepeatHeightCm is > 0)
        {
            var rw = (double)request.Poster.RepeatWidthCm.Value;
            var rh = (double)request.Poster.RepeatHeightCm.Value;
            var tileW = Math.Max(1, (int)Math.Round(rw * pxPerCm));
            var tileH = Math.Max(1, (int)Math.Round(rh * pxPerCm));
            using var tile = source.Clone(x => x.Resize(tileW, tileH));
            foreach (var t in WallLayout.TilePattern(prodW, prodH, rw, rh, request.Poster.RepeatType))
            {
                if (t.XCm + t.WidthCm < wx0 || t.XCm > wx0 + w || t.YCm + t.HeightCm < y0 || t.YCm > y0 + h) continue;
                var at = new Point((int)Math.Round((t.XCm - wx0) * pxPerCm), (int)Math.Round((t.YCm - y0) * pxPerCm));
                result.Mutate(x => x.DrawImage(tile, at, 1f));
            }
        }
        else
        {
            // Kaynak piksel dikdörtgeni: tam üretim alanına karşılık gelen kaynak alan (kırpma pay kadar genişletilmiş).
            double sx, sy, sw, sh;
            if (cfg.Fit == FitMode.Stretch)
            {
                (sx, sy, sw, sh) = (0, 0, source.Width, source.Height);
            }
            else
            {
                var crop = cfg.Crop ?? WallPreviewRenderer.DefaultCrop(source.Width, source.Height, (double)cfg.WidthCm / (double)cfg.HeightCm);
                var cw = (double)crop.W * prodW / (double)cfg.WidthCm;
                var ch = (double)crop.H * prodH / (double)cfg.HeightCm;
                var cx = (double)crop.X + (double)crop.W / 2 - cw / 2;
                var cy = (double)crop.Y + (double)crop.H / 2 - ch / 2;
                // Görselin dışına taşarsa içeri kaydırılır; yine sığmazsa kenar pikselleri esner (VARSAYIM).
                cw = Math.Min(cw, 1); ch = Math.Min(ch, 1);
                cx = Math.Clamp(cx, 0, 1 - cw); cy = Math.Clamp(cy, 0, 1 - ch);
                (sx, sy, sw, sh) = (cx * source.Width, cy * source.Height, cw * source.Width, ch * source.Height);
            }

            var rect = new Rectangle(
                (int)Math.Floor(sx + wx0 / prodW * sw), (int)Math.Floor(sy + y0 / prodH * sh),
                Math.Max(1, (int)Math.Ceiling(w / prodW * sw)), Math.Max(1, (int)Math.Ceiling(h / prodH * sh)));
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, source.Width, source.Height));
            if (rect.Width > 0 && rect.Height > 0)
            {
                using var part = source.Clone(x => x.Crop(rect).Resize(outW, outH, KnownResamplers.Lanczos3));
                result.Mutate(x => x.DrawImage(part, new Point(0, 0), 1f));
            }
        }

        result.Mutate(x =>
        {
            if (cfg.Mirror) x.Flip(FlipMode.Horizontal);
            if (cfg.Filter == ImageFilter.Grayscale) x.Grayscale();
            else if (cfg.Filter == ImageFilter.Sepia) x.Sepia();
        });
        return result;
    }

    private static Image<Rgba32> AddMarginsAndMarks(Image<Rgba32> panel, double pxPerCm, int index, int count, ProductionRenderRequest request, double widthCm, double heightCm, int dpi)
    {
        var m = (int)Math.Round(MarginCm * pxPerCm);
        var page = new Image<Rgba32>(panel.Width + 2 * m, panel.Height + 2 * m, Color.White);
        var markLen = m * 0.8f;
        var stroke = Math.Max(1f, (float)pxPerCm * 0.03f);
        var family = Fonts();

        page.Mutate(ctx =>
        {
            ctx.DrawImage(panel, new Point(m, m), 1f);
            var black = Color.Black;
            // Köşe kesim işaretleri (bleed alanının dışında, kenar boşluğunda).
            foreach (var (x, y) in new[] { (m, m), (m + panel.Width, m), (m, m + panel.Height), (m + panel.Width, m + panel.Height) })
            {
                var dx = x == m ? -1 : 1;
                var dy = y == m ? -1 : 1;
                ctx.DrawLine(black, stroke, new PointF(x + dx * 4, y), new PointF(x + dx * (4 + markLen), y));
                ctx.DrawLine(black, stroke, new PointF(x, y + dy * 4), new PointF(x, y + dy * (4 + markLen)));
            }
            // Üst ve alt ortada hizalama (registration) işareti - komşu panellerle aynı yükseklikte.
            foreach (var cy in new[] { m / 2f, m + panel.Height + m / 2f })
            {
                var cx = page.Width / 2f;
                var r = m * 0.25f;
                ctx.Draw(black, stroke, new EllipsePolygon(new PointF(cx, cy), r));
                ctx.DrawLine(black, stroke, new PointF(cx - r * 1.6f, cy), new PointF(cx + r * 1.6f, cy));
                ctx.DrawLine(black, stroke, new PointF(cx, cy - r * 1.6f), new PointF(cx, cy + r * 1.6f));
            }
            if (family is { } f)
            {
                var font = f.CreateFont(Math.Max(10f, m * 0.28f), FontStyle.Bold);
                var label = string.Create(CultureInfo.GetCultureInfo("tr-TR"),
                    $"PANEL {index}/{count} · {request.OrderNumber} · {request.ItemLabel} · {widthCm:0.#}×{heightCm:0.#} cm · {dpi} DPI");
                ctx.DrawText(label, font, black, new PointF(m, m * 0.15f));
                ctx.DrawText($"← {index - 1}", font, Color.Gray, new PointF(4, page.Height / 2f));
                ctx.DrawText($"{index + 1} →", font, Color.Gray, new PointF(page.Width - m + 4, page.Height / 2f));
            }
        });
        return page;
    }

    private static FontFamily? _family;
    private static FontFamily? Fonts() =>
        _family ??= SystemFonts.Families.FirstOrDefault(f => f.Name is "Arial" or "Segoe UI" or "DejaVu Sans" or "Liberation Sans");

    private async Task<Image<Rgba32>> LoadAsync(PosterSource source, CancellationToken cancellationToken)
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

/// <summary>Sayfa başına tek JPEG gömen, bağımlılıksız en küçük PDF 1.4 yazıcısı (DCTDecode).</summary>
public static class MinimalPdf
{
    public static byte[] Build(IReadOnlyList<(byte[] Jpeg, int W, int H, double WidthPt, double HeightPt)> pages, string title)
    {
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void Write(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        void Obj(int id, Action body) { offsets.Add(ms.Position); Write($"{id} 0 obj\n"); body(); Write("\nendobj\n"); }
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        Write("%PDF-1.4\n%âãÏÓ\n");
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{4 + i * 3} 0 R"));
        Obj(1, () => Write("<< /Type /Catalog /Pages 2 0 R >>"));
        Obj(2, () => Write($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"));
        var safeTitle = new string(title.Select(c => c < 128 && c != '(' && c != ')' && c != '\\' ? c : '_').ToArray());
        Obj(3, () => Write($"<< /Title ({safeTitle}) /Producer (Dekorras) >>"));

        for (var i = 0; i < pages.Count; i++)
        {
            var (jpeg, w, h, wPt, hPt) = pages[i];
            var pageId = 4 + i * 3;
            var content = $"q {F(wPt)} 0 0 {F(hPt)} 0 0 cm /Im0 Do Q";
            Obj(pageId, () => Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(wPt)} {F(hPt)}] /Resources << /XObject << /Im0 {pageId + 2} 0 R >> >> /Contents {pageId + 1} 0 R >>"));
            Obj(pageId + 1, () => Write($"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
            Obj(pageId + 2, () =>
            {
                Write($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
                ms.Write(jpeg);
                Write("\nendstream");
            });
        }

        var xref = ms.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
