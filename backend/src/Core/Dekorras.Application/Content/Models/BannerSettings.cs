using System.Text.Json;

namespace Dekorras.Application.Content.Models;

/// <summary>
/// BannerNode.SettingsJson'ın (düzen ayarları) C# karşılığı - spesifikasyonun breakpoint bazlı
/// grid önerisinin sadeleştirilmiş hali. Her breakpoint alanı null ise o breakpoint'te üstteki
/// (daha küçük) breakpoint'in değeri Bootstrap'in kendi cascade davranışıyla geçerli kalır.
/// </summary>
public sealed class BannerNodeSettings
{
    public int? ColXs { get; set; }
    public int? ColSm { get; set; }
    public int? ColMd { get; set; }
    public int? ColLg { get; set; }
    public int? ColXl { get; set; }
    public int? ColXxl { get; set; }

    public int? Offset { get; set; }
    public int? Order { get; set; }
    public bool Hidden { get; set; }

    public string? BackgroundColor { get; set; }
    public string? BackgroundImageUrl { get; set; }
    public string? TextColor { get; set; }
    public string? PaddingY { get; set; }
    public string? PaddingX { get; set; }
    public string? MinHeight { get; set; }
    public string? BorderRadius { get; set; }
    public string? TextAlign { get; set; }
}

/// <summary>BannerContent.SettingsJson'ın C# karşılığı - içerik tipine özel ekstra ayarlar.</summary>
public sealed class BannerContentSettings
{
    /// <summary>ContentType.ProductWidget için: hangi kategori, kaç ürün gösterilecek.</summary>
    public Guid? CategoryId { get; set; }
    public int? ProductCount { get; set; }

    /// <summary>ContentType.Heading için başlık seviyesi (2-6).</summary>
    public int? HeadingLevel { get; set; }

    public string? TextColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? ButtonStyle { get; set; }

    /// <summary>
    /// ContentType.Image için: "Alternatif Metin" alanı doluysa görselin ÜZERİNE bindirilen metnin
    /// rengi ve yatay hizalaması ("left"/"center"/"right"). Mevcut TextColor alanından BİLEREK
    /// AYRI tutuldu - TextColor zaten Heading/ProductWidget gibi başka bağlamlarda kullanılıyor,
    /// aynı alanı Image'ın metin bindirmesi için de kullanmak anlam karışıklığına yol açardı.
    /// </summary>
    public string? AltTextColor { get; set; }
    public string? AltTextAlign { get; set; }
}

public static class BannerSettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }
}
