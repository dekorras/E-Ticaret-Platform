using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Dekorras.E2ETests;

internal static class TestImages
{
    /// <summary>Oda fotoğrafı yerine kullanılan düz renkli JPEG.</summary>
    /// <summary>Krem rengi duvar + önünde koyu kahverengi "koltuk" (x %55–75, y %50–95) ve üstte hafif ışık gradyanı.</summary>
    public static byte[] RoomWithChairJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var fx = x / (double)width;
                    var fy = y / (double)height;
                    var light = (byte)Math.Clamp(236 - fx * 14, 0, 255); // hafif yan ışık
                    row[x] = fx is >= 0.55 and <= 0.75 && fy is >= 0.50 and <= 0.95
                        ? new Rgba32(84, 52, 34)
                        : new Rgba32(light, (byte)(light - 6), (byte)(light - 16));
                }
            }
        });
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    public static byte[] SolidJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(226, 222, 214));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }
}
