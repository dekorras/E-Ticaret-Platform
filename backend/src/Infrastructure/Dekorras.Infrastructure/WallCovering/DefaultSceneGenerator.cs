using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Dekorras.Infrastructure.WallCovering;

/// <summary>Gerçek oda fotoğrafları admin'den yüklenene kadar kullanılan prosedürel sahneler.
/// Her sahne üç katman üretir: taban (boş duvarlı oda, JPEG), gölge haritası (gri tonlu; 255 = ışık
/// değişmez, koyu = gölge; PNG) ve ön plan maskesi (duvarın önündeki mobilya, saydam PNG).
/// Mobilya hem tabana hem maskeye çizilir: taban tutarlı görünür, maske posteri mobilyanın ARKASINA alır.
/// VARSAYIM: gerçek fotoğraflı sahneler bunların yerini alacak; bu yüzden basit geometrik çizimler yeterli.</summary>
public sealed class DefaultSceneGenerator(IWallImageStore store) : IDefaultSceneGenerator
{
    private const int W = 1600;
    private const int H = 1000;

    public async Task<IReadOnlyList<GeneratedScene>> GenerateAsync(CancellationToken cancellationToken) =>
    [
        await SaveAsync("modern-salon", "Modern Salon", RoomType.LivingRoom, LivingRoom(), 400m, 250m, cancellationToken),
        await SaveAsync("yatak-odasi", "Yatak Odası (açılı)", RoomType.Bedroom, Bedroom(), 360m, 260m, cancellationToken),
        await SaveAsync("calisma-odasi", "Çalışma Odası", RoomType.Office, Office(), 320m, 250m, cancellationToken),
    ];

    private sealed record SceneLayers(Image<Rgba32> Base, Image<L8> Shadow, Image<Rgba32> Mask, WallQuad Quad);

    private async Task<GeneratedScene> SaveAsync(string slug, string name, RoomType type, SceneLayers layers, decimal realW, decimal realH, CancellationToken ct)
    {
        using (layers.Base)
        using (layers.Shadow)
        using (layers.Mask)
        {
            var stamp = DateTime.UtcNow.Ticks.ToString("x");
            var baseUrl = await store.SavePublicAsync($"wall/scenes/{slug}-{stamp}-base.jpg", ToJpeg(layers.Base), ct);
            var shadowUrl = await store.SavePublicAsync($"wall/scenes/{slug}-{stamp}-shadow.png", ToPng(layers.Shadow), ct);
            var maskUrl = await store.SavePublicAsync($"wall/scenes/{slug}-{stamp}-mask.png", ToPng(layers.Mask), ct);
            return new GeneratedScene(name, type, baseUrl, shadowUrl, maskUrl, W, H, layers.Quad, realW, realH);
        }
    }

    // ---------------- Sahneler ----------------

    private static SceneLayers LivingRoom()
    {
        var quad = new WallQuad(new Point2(300, 90), new Point2(1300, 90), new Point2(1300, 715), new Point2(300, 715));
        var img = new Image<Rgba32>(W, H);
        img.Mutate(ctx =>
        {
            VerticalGradient(ctx, new RectangularPolygon(0, 0, W, 780), 0, 780, Color.ParseHex("f1ece4"), Color.ParseHex("e2dbd0"));
            ctx.Fill(Color.ParseHex("fbfaf7"), new RectangularPolygon(0, 0, W, 34));
            Floor(ctx, 780);
            ctx.Fill(Color.ParseHex("fdfdfd"), new RectangularPolygon(0, 766, W, 18));
        });

        var mask = new Image<Rgba32>(W, H);
        foreach (var target in new[] { img, mask })
        {
            target.Mutate(ctx =>
            {
                // Kanepe
                var sofa = Color.ParseHex("4d5866");
                var sofaDark = Color.ParseHex("3d4652");
                ctx.Fill(sofaDark, new RectangularPolygon(420, 560, 760, 170));   // sırt
                ctx.Fill(sofa, new RectangularPolygon(400, 690, 800, 150));      // oturma
                ctx.Fill(sofaDark, new RectangularPolygon(350, 640, 90, 220));   // kolçak sol
                ctx.Fill(sofaDark, new RectangularPolygon(1160, 640, 90, 220));  // kolçak sağ
                ctx.Fill(Color.ParseHex("5e6b7a"), new RectangularPolygon(440, 600, 350, 110));
                ctx.Fill(Color.ParseHex("5e6b7a"), new RectangularPolygon(810, 600, 350, 110));
                ctx.Fill(Color.ParseHex("c9a86a"), new RectangularPolygon(700, 640, 110, 70));   // yastık
                foreach (var x in new[] { 380, 1200 }) ctx.Fill(Color.ParseHex("2b2b2b"), new RectangularPolygon(x, 860, 18, 40));
                // Lambader
                ctx.Fill(Color.ParseHex("333333"), new RectangularPolygon(1418, 330, 8, 600));
                ctx.Fill(Color.ParseHex("333333"), new EllipsePolygon(new PointF(1422, 930), new SizeF(90, 18)));
                ctx.Fill(Color.ParseHex("efe3c8"), Poly(new(1360, 250), new(1484, 250), new(1455, 340), new(1389, 340)));
                // Saksı + bitki
                ctx.Fill(Color.ParseHex("a15c3e"), Poly(new(150, 820), new(250, 820), new(235, 930), new(165, 930)));
                foreach (var (cx, cy, rx, ry) in new[] { (200, 700, 70, 130), (150, 760, 60, 90), (255, 760, 60, 95), (205, 640, 40, 80) })
                    ctx.Fill(Color.ParseHex("3f7d4a"), new EllipsePolygon(new PointF(cx, cy), new SizeF(rx * 2, ry * 2)));
            });
        }

        var shadow = BaseShadow();
        shadow.Mutate(ctx =>
        {
            ctx.Fill(Gray(150), new EllipsePolygon(new PointF(800, 735), new SizeF(1000, 150)));   // kanepe arkası
            ctx.Fill(Gray(185), new EllipsePolygon(new PointF(200, 700), new SizeF(220, 360)));    // bitki
            ctx.Fill(Gray(255), new EllipsePolygon(new PointF(1420, 280), new SizeF(520, 420)));   // lamba ışığı
            ctx.GaussianBlur(45);
        });
        Vignette(shadow);
        return new SceneLayers(img, shadow, mask, quad);
    }

    private static SceneLayers Bedroom()
    {
        // Duvar sağa doğru izleyiciye yaklaşıyor: perspektif (homografi) gerçekten sınanır.
        var quad = new WallQuad(new Point2(430, 120), new Point2(1340, 50), new Point2(1340, 800), new Point2(430, 700));
        var img = new Image<Rgba32>(W, H);
        img.Mutate(ctx =>
        {
            ctx.Fill(Color.ParseHex("f7f5f2"), new RectangularPolygon(0, 0, W, H));
            ctx.Fill(Color.ParseHex("d9d3cb"), Poly(new(0, 0), new(430, 120), new(430, 700), new(0, 1000)));             // sol yan duvar
            HorizontalGradient(ctx, Poly(new(430, 120), new(1340, 50), new(1340, 800), new(430, 700)), 430, 1340, Color.ParseHex("d7dee6"), Color.ParseHex("e8edf2"));
            ctx.Fill(Color.ParseHex("eef1f4"), Poly(new(1340, 50), new(1600, 20), new(1600, 1000), new(1340, 800)));      // sağ duvar
            ctx.Fill(Color.ParseHex("b99873"), Poly(new(430, 700), new(1340, 800), new(1600, 1000), new(0, 1000)));       // zemin
            ctx.Fill(Color.ParseHex("fafafa"), Poly(new(430, 688), new(1340, 786), new(1340, 800), new(430, 700)));       // süpürgelik
            // Pencere ışığı (sağ duvar)
            ctx.Fill(Color.ParseHex("fdfcf6"), Poly(new(1420, 180), new(1560, 160), new(1560, 560), new(1420, 580)));
        });

        var mask = new Image<Rgba32>(W, H);
        foreach (var target in new[] { img, mask })
        {
            target.Mutate(ctx =>
            {
                ctx.Fill(Color.ParseHex("8a6f55"), Poly(new(600, 470), new(1180, 420), new(1180, 760), new(600, 690)));    // başlık
                ctx.Fill(Color.ParseHex("f3f0ea"), Poly(new(560, 680), new(1230, 740), new(1480, 960), new(330, 900)));    // yatak
                ctx.Fill(Color.ParseHex("9fb4c7"), Poly(new(470, 790), new(1330, 850), new(1520, 1000), new(260, 1000)));  // örtü
                ctx.Fill(Color.ParseHex("ffffff"), Poly(new(640, 640), new(860, 655), new(850, 720), new(620, 700)));      // yastık
                ctx.Fill(Color.ParseHex("ffffff"), Poly(new(900, 660), new(1120, 680), new(1120, 745), new(890, 725)));
                ctx.Fill(Color.ParseHex("6b5846"), Poly(new(430, 640), new(560, 650), new(560, 790), new(430, 770)));      // komodin
                ctx.Fill(Color.ParseHex("e8d9b5"), new EllipsePolygon(new PointF(495, 600), new SizeF(70, 60)));
            });
        }

        var shadow = BaseShadow();
        shadow.Mutate(ctx =>
        {
            // Işık sağdaki pencereden: duvarın solu daha karanlık.
            HorizontalGradient(ctx, new RectangularPolygon(430, 0, 910, H), 430, 1340, Gray(190), Gray(250));
            ctx.Fill(Gray(150), Poly(new(580, 460), new(1200, 410), new(1200, 700), new(580, 680)));
            ctx.GaussianBlur(35);
        });
        return new SceneLayers(img, shadow, mask, quad);
    }

    private static SceneLayers Office()
    {
        var quad = new WallQuad(new Point2(310, 60), new Point2(1290, 60), new Point2(1290, 826), new Point2(310, 826));
        var img = new Image<Rgba32>(W, H);
        img.Mutate(ctx =>
        {
            VerticalGradient(ctx, new RectangularPolygon(0, 0, W, 850), 0, 850, Color.ParseHex("eceeee"), Color.ParseHex("dfe2e2"));
            Floor(ctx, 850, Color.ParseHex("7d7f82"), Color.ParseHex("5d6064"));
            ctx.Fill(Color.ParseHex("f9f9f9"), new RectangularPolygon(0, 836, W, 16));
        });

        var mask = new Image<Rgba32>(W, H);
        foreach (var target in new[] { img, mask })
        {
            target.Mutate(ctx =>
            {
                ctx.Fill(Color.ParseHex("c8a27a"), new RectangularPolygon(360, 600, 880, 34));      // masa
                ctx.Fill(Color.ParseHex("2f2f2f"), new RectangularPolygon(390, 634, 22, 300));
                ctx.Fill(Color.ParseHex("2f2f2f"), new RectangularPolygon(1190, 634, 22, 300));
                ctx.Fill(Color.ParseHex("1e1e1e"), new RectangularPolygon(700, 440, 260, 160));     // monitör
                ctx.Fill(Color.ParseHex("2f2f2f"), new RectangularPolygon(815, 600, 30, 10));
                ctx.Fill(Color.ParseHex("262a30"), new RectangularPolygon(760, 700, 200, 230));     // sandalye sırtı
                ctx.Fill(Color.ParseHex("262a30"), new RectangularPolygon(740, 860, 240, 40));
                ctx.Fill(Color.ParseHex("3d7a48"), new EllipsePolygon(new PointF(470, 540), new SizeF(90, 120)));
                ctx.Fill(Color.ParseHex("ffffff"), new RectangularPolygon(445, 570, 50, 30));
            });
        }

        var shadow = BaseShadow();
        shadow.Mutate(ctx =>
        {
            ctx.Fill(Gray(165), new RectangularPolygon(340, 590, 920, 60));
            ctx.Fill(Gray(175), new EllipsePolygon(new PointF(830, 560), new SizeF(360, 160)));
            ctx.GaussianBlur(30);
        });
        Vignette(shadow);
        return new SceneLayers(img, shadow, mask, quad);
    }

    // ---------------- Yardımcılar ----------------

    private static Image<L8> BaseShadow() => new(W, H, new L8(245));

    private static Color Gray(byte v) => Color.FromRgb(v, v, v);

    private static void Vignette(Image<L8> shadow) => shadow.Mutate(ctx => ctx.Vignette(Gray(200)));

    private static void Floor(IImageProcessingContext ctx, int top, Color? light = null, Color? dark = null)
    {
        VerticalGradient(ctx, new RectangularPolygon(0, top, W, H - top), top, H, light ?? Color.ParseHex("b98b5b"), dark ?? Color.ParseHex("8a633f"));
        for (var y = top + 30; y < H; y += 34)
            ctx.DrawLine(Color.Black.WithAlpha(0.08f), 2f, new PointF(0, y), new PointF(W, y));
    }

    private static void VerticalGradient(IImageProcessingContext ctx, IPath path, float from, float to, Color a, Color b) =>
        ctx.Fill(new LinearGradientBrush(new PointF(0, from), new PointF(0, to), GradientRepetitionMode.None, new ColorStop(0, a), new ColorStop(1, b)), path);

    private static void HorizontalGradient(IImageProcessingContext ctx, IPath path, float from, float to, Color a, Color b) =>
        ctx.Fill(new LinearGradientBrush(new PointF(from, 0), new PointF(to, 0), GradientRepetitionMode.None,
            new ColorStop(0, a), new ColorStop(1, b)), path);

    private static IPath Poly(params PointF[] points) => new Polygon(new LinearLineSegment(points));

    private static byte[] ToJpeg(Image image)
    {
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }

    private static byte[] ToPng(Image image)
    {
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
