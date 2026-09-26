using Dekorras.Application.WallCovering;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Dekorras.Infrastructure.WallCovering;

public sealed class ImageDerivativeService(IWallImageStore store) : IImageDerivativeService
{
    public const int ThumbWidth = 400;
    public const int ListWidth = 800;
    public const int PreviewWidth = 2000;

    private static readonly JpegEncoder Jpeg = new() { Quality = 84 };

    public ImageInspection Inspect(Stream content)
    {
        try
        {
            var start = content.CanSeek ? content.Position : 0;
            // Biçim uzantıdan değil İÇERİKTEN (sihirli baytlar) belirlenir.
            var format = Image.DetectFormat(content);
            if (content.CanSeek) content.Position = start;
            var info = Image.Identify(content);
            if (content.CanSeek) content.Position = start;

            var extension = format switch
            {
                JpegFormat => ".jpg",
                PngFormat => ".png",
                WebpFormat => ".webp",
                _ => ""
            };
            if (extension.Length == 0)
                return new ImageInspection(false, "Yalnızca JPG, PNG veya WebP görseller kabul edilir.", 0, 0, format.Name, "");
            if (info.Width < 10 || info.Height < 10 || (long)info.Width * info.Height > 150_000_000)
                return new ImageInspection(false, "Görsel boyutu desteklenmiyor.", info.Width, info.Height, format.Name, extension);

            return new ImageInspection(true, null, info.Width, info.Height, format.Name, extension);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return new ImageInspection(false, "Dosya geçerli bir görsel değil.", 0, 0, null, "");
        }
    }

    public async Task<ImageDerivatives> CreateDerivativesAsync(Stream original, string folder, string baseName, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(original, cancellationToken);
        image.Mutate(x => x.AutoOrient());

        int? dpi = null;
        var meta = image.Metadata;
        if (meta.HorizontalResolution > 1)
        {
            dpi = meta.ResolutionUnits switch
            {
                PixelResolutionUnit.PixelsPerInch => (int)Math.Round(meta.HorizontalResolution),
                PixelResolutionUnit.PixelsPerCentimeter => (int)Math.Round(meta.HorizontalResolution * 2.54),
                _ => null
            };
        }

        var prefix = $"wall/{folder}/{baseName}";
        var thumb = await store.SavePublicAsync($"{prefix}-{ThumbWidth}.jpg", Encode(Resize(image, ThumbWidth)), cancellationToken);
        var list = await store.SavePublicAsync($"{prefix}-{ListWidth}.jpg", Encode(Resize(image, ListWidth)), cancellationToken);

        using var preview = Resize(image, PreviewWidth);
        Watermark(preview);
        var previewUrl = await store.SavePublicAsync($"{prefix}-{PreviewWidth}.jpg", Encode(preview, dispose: false), cancellationToken);

        return new ImageDerivatives(image.Width, image.Height, dpi, thumb, list, previewUrl, Lqip(image), DominantColors(image));
    }

    public async Task<(byte[] Content, int WidthPx, int HeightPx)> SanitizeAsync(Stream content, int maxLongEdgePx, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(content, cancellationToken);
        image.Mutate(x => x.AutoOrient());
        // EXIF (konum, cihaz bilgisi), IPTC ve XMP silinir - yalnızca pikseller yeniden kodlanır.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IccProfile = null;

        var longEdge = Math.Max(image.Width, image.Height);
        if (longEdge > maxLongEdgePx)
        {
            var scale = maxLongEdgePx / (double)longEdge;
            image.Mutate(x => x.Resize((int)Math.Round(image.Width * scale), (int)Math.Round(image.Height * scale)));
        }

        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 88 }, cancellationToken);
        return (ms.ToArray(), image.Width, image.Height);
    }

    /// <summary>Gölge haritası = duvarın YALNIZCA ışık/gölge dağılımı (poster bununla multiply edilir).
    /// Duvarın önündeki koyu eşyalar (koltuk, çerçeve, bitki) gölge SAYILMAZ: 1) kullanıcının işaretlediği ön plan
    /// maskesi, 2) duvarın tipik parlaklığından belirgin sapan pikseller ve 3) duvar dörtgeninin dışı, bulanıklaştırmadan
    /// ÖNCE duvarın ortanca parlaklığıyla doldurulur; aksi hâlde eşya posterin üstüne koyu bir leke olarak yayılır.
    /// Karartma da gerçekçi bir aralıkla (en fazla ~%30) sınırlanır.</summary>
    public async Task<byte[]> CreateShadowMapAsync(byte[] photo, Domain.WallCovering.WallQuad wallQuad, byte[]? foregroundMaskPng, CancellationToken cancellationToken)
    {
        using var image = Image.Load<L8>(photo);
        var w = image.Width;
        var h = image.Height;
        var corners = wallQuad.Corners;

        // Dörtgen içi testi (dışbükey; köşe sırası saat yönü ya da tersi olabilir).
        bool InQuad(double x, double y)
        {
            var sign = 0;
            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
                var s = Math.Sign(cross);
                if (s == 0) continue;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }

        bool[]? masked = null;
        if (foregroundMaskPng is { Length: > 0 })
        {
            using var mask = Image.Load<Rgba32>(foregroundMaskPng);
            if (mask.Width != w || mask.Height != h) mask.Mutate(x => x.Resize(w, h));
            masked = new bool[w * h];
            var m = masked;
            mask.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++) m[y * w + x] = row[x].A > 64;
                }
            });
        }

        // Duvarın "nötr ışık" parlaklığı: dörtgen İÇİNDEKİ, işaretli eşya olmayan piksellerin ortancası.
        var inside = new bool[w * h];
        var samples = new List<byte>();
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var idx = y * w + x;
                    if (!InQuad(x + 0.5, y + 0.5)) continue;
                    inside[idx] = true;
                    if ((x & 3) == 0 && (y & 3) == 0 && masked?[idx] != true) samples.Add(row[x].PackedValue);
                }
            }
        });
        samples.Sort();
        var median = Math.Max(1, samples.Count == 0 ? 200 : (int)samples[samples.Count / 2]);

        // Eşyalar ve dörtgen dışı nötrlenir: yalnızca duvarın kendi yumuşak ışık değişimi kalır.
        // VARSAYIM: duvar parlaklığından %28'den fazla sapan piksel duvara ait değildir (eşya/çerçeve/priz).
        var tolerance = median * 0.28;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var idx = y * w + x;
                    if (!inside[idx] || masked?[idx] == true || Math.Abs(row[x].PackedValue - median) > tolerance)
                        row[x] = new L8((byte)median);
                }
            }
        });

        // Yüksek frekanslı doku (duvar deseni, lambri çizgileri) silinsin, yalnızca ışık/gölge kalsın.
        image.Mutate(x => x.GaussianBlur(Math.Max(6f, w / 60f)));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                foreach (ref var p in accessor.GetRowSpan(y))
                {
                    // Ortanca parlaklık ≈ 245 (hafif karartma); gölge en fazla ~%30, aydınlatma üst sınırı 255.
                    var factor = p.PackedValue / (double)median;
                    p = new L8((byte)Math.Clamp(245 * factor, 175, 255));
                }
            }
        });

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    public async Task<byte[]> NormalizeMaskAsync(Stream maskPng, int widthPx, int heightPx, CancellationToken cancellationToken)
    {
        var format = Image.DetectFormat(maskPng);
        if (format is not PngFormat) throw new InvalidImageContentException("Maske PNG olmalıdır.");
        maskPng.Position = 0;
        using var mask = await Image.LoadAsync<Rgba32>(maskPng, cancellationToken);
        if (mask.Width != widthPx || mask.Height != heightPx)
            mask.Mutate(x => x.Resize(widthPx, heightPx));
        mask.Metadata.ExifProfile = null;
        using var ms = new MemoryStream();
        await mask.SaveAsPngAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private static Image<Rgba32> Resize(Image<Rgba32> source, int width)
    {
        var clone = source.Clone();
        if (clone.Width > width)
            clone.Mutate(x => x.Resize(width, 0, KnownResamplers.Lanczos3));
        return clone;
    }

    private static byte[] Encode(Image<Rgba32> image, bool dispose = true)
    {
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, Jpeg);
        if (dispose) image.Dispose();
        return ms.ToArray();
    }

    private static string Lqip(Image<Rgba32> image)
    {
        using var tiny = image.Clone(x => x.Resize(24, 0).GaussianBlur(1.2f));
        using var ms = new MemoryStream();
        tiny.SaveAsJpeg(ms, new JpegEncoder { Quality = 45 });
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Basit renk kümeleme: 64×64 örneklem, kanal başına 32 seviyeye nicemleme, en sık
    /// kovalardan birbirine yeterince uzak (RGB mesafesi &gt; 48) en fazla 5 renk.</summary>
    public static IReadOnlyList<string> DominantColors(Image<Rgba32> image)
    {
        using var sample = image.Clone(x => x.Resize(64, 64));
        var buckets = new Dictionary<int, (long R, long G, long B, int Count)>();
        sample.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                foreach (ref var p in accessor.GetRowSpan(y))
                {
                    if (p.A < 128) continue;
                    var key = (p.R >> 3 << 10) | (p.G >> 3 << 5) | (p.B >> 3);
                    buckets.TryGetValue(key, out var b);
                    buckets[key] = (b.R + p.R, b.G + p.G, b.B + p.B, b.Count + 1);
                }
            }
        });

        var chosen = new List<(int R, int G, int B)>();
        foreach (var b in buckets.Values.OrderByDescending(b => b.Count))
        {
            var c = ((int)(b.R / b.Count), (int)(b.G / b.Count), (int)(b.B / b.Count));
            if (chosen.All(o => Math.Sqrt(Math.Pow(o.R - c.Item1, 2) + Math.Pow(o.G - c.Item2, 2) + Math.Pow(o.B - c.Item3, 2)) > 48))
                chosen.Add(c);
            if (chosen.Count == 5) break;
        }
        return chosen.Select(c => $"#{c.R:x2}{c.G:x2}{c.B:x2}").ToList();
    }

    private static FontFamily? _watermarkFamily;

    /// <summary>Çapraz, yarı saydam "dekorras.com" deseni. Sistemde yazı tipi bulunamazsa ince çapraz çizgiler.</summary>
    public static void Watermark(Image<Rgba32> image)
    {
        _watermarkFamily ??= SystemFonts.Families.FirstOrDefault(f => f.Name is "Arial" or "Segoe UI" or "DejaVu Sans" or "Liberation Sans");
        var color = Color.White.WithAlpha(0.28f);
        var shadow = Color.Black.WithAlpha(0.12f);
        var size = Math.Max(14f, image.Width / 22f);

        image.Mutate(ctx =>
        {
            if (_watermarkFamily is { } family && family.Name is not null)
            {
                var font = family.CreateFont(size, FontStyle.Bold);
                var stepX = size * 9;
                var stepY = size * 5;
                for (var y = -image.Height; y < image.Height * 2; y += (int)stepY)
                for (var x = -image.Width; x < image.Width * 2; x += (int)stepX)
                {
                    var origin = new PointF(x + (y / (int)stepY % 2) * stepX / 2, y);
                    var options = new DrawingOptions { Transform = System.Numerics.Matrix3x2.CreateRotation(-0.5f, origin) };
                    ctx.DrawText(options, "dekorras.com", font, shadow, origin + new PointF(1.5f, 1.5f));
                    ctx.DrawText(options, "dekorras.com", font, color, origin);
                }
            }
            else
            {
                for (var i = -image.Height; i < image.Width; i += 60)
                    ctx.DrawLine(color, 1.5f, new PointF(i, image.Height), new PointF(i + image.Height, 0));
            }
        });
    }
}
