using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>Görselin kırpma alanı, orijinal görsele göre NORMALİZE (0–1) koordinatlarda.
/// Piksel karşılığı görsel boyutu bilindiğinde <see cref="ToPixels"/> ile hesaplanır.</summary>
public sealed record CropRect(decimal X, decimal Y, decimal W, decimal H)
{
    public static readonly CropRect Full = new(0m, 0m, 1m, 1m);

    public bool IsValid =>
        X >= 0 && Y >= 0 && W > 0 && H > 0 && X + W <= 1.0001m && Y + H <= 1.0001m;

    public (int X, int Y, int W, int H) ToPixels(int imageWidthPx, int imageHeightPx) => (
        (int)Math.Round(X * imageWidthPx, MidpointRounding.AwayFromZero),
        (int)Math.Round(Y * imageHeightPx, MidpointRounding.AwayFromZero),
        Math.Max(1, (int)Math.Round(W * imageWidthPx, MidpointRounding.AwayFromZero)),
        Math.Max(1, (int)Math.Round(H * imageHeightPx, MidpointRounding.AwayFromZero)));

    public string ToQueryValue() => string.Join(",", new[] { X, Y, W, H }.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)));

    public static bool TryParse(string? value, out CropRect crop)
    {
        crop = Full;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;

        var numbers = new decimal[4];
        for (var i = 0; i < 4; i++)
        {
            if (!decimal.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        var candidate = new CropRect(numbers[0], numbers[1], numbers[2], numbers[3]);
        if (!candidate.IsValid) return false;

        crop = candidate;
        return true;
    }
}

/// <summary>Müşterinin seçtiği konfigürasyon (spec 1.2 - Configuration value object). Sepette ve
/// siparişte JSON olarak saklanır; <see cref="Hash"/> aynı ürünün farklı ölçülerini sepette ayrı
/// satır yapan eşleşme anahtarıdır.</summary>
public sealed class WallConfiguration : ValueObject
{
    public decimal WidthCm { get; init; }
    public decimal HeightCm { get; init; }
    public LengthUnit Unit { get; init; } = LengthUnit.Cm;
    public string MaterialCode { get; init; } = default!;
    public FitMode Fit { get; init; } = FitMode.Crop;
    public bool Mirror { get; init; }
    public ImageFilter Filter { get; init; } = ImageFilter.None;
    public CropRect? Crop { get; init; }

    public WallConfiguration() { }

    public WallConfiguration(decimal widthCm, decimal heightCm, string materialCode, LengthUnit unit = LengthUnit.Cm,
        FitMode fit = FitMode.Crop, bool mirror = false, ImageFilter filter = ImageFilter.None, CropRect? crop = null)
    {
        WidthCm = Math.Round(widthCm, 1, MidpointRounding.AwayFromZero);
        HeightCm = Math.Round(heightCm, 1, MidpointRounding.AwayFromZero);
        MaterialCode = materialCode;
        Unit = unit;
        Fit = fit;
        Mirror = mirror;
        Filter = filter;
        // Esnet modunda kırpma alanı anlamsızdır - hash'i gereksiz yere farklılaştırmasın diye atılır.
        Crop = fit == FitMode.Stretch ? null : crop;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static WallConfiguration? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<WallConfiguration>(json, JsonOptions);

    /// <summary>Kanonik gösterimin SHA-256 özetinin ilk 32 hex karakteri. Girilen birim (Unit)
    /// BİLİNÇLİ OLARAK dahil edilmez: 300 cm ile 3 m aynı üretimdir, sepette tek satır olmalı.</summary>
    public string Hash()
    {
        var canonical = string.Join("|",
            WidthCm.ToString("0.0", CultureInfo.InvariantCulture),
            HeightCm.ToString("0.0", CultureInfo.InvariantCulture),
            MaterialCode,
            Fit,
            Mirror ? "1" : "0",
            Filter,
            Crop?.ToQueryValue() ?? "-");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32].ToLowerInvariant();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return WidthCm;
        yield return HeightCm;
        yield return MaterialCode;
        yield return Fit;
        yield return Mirror;
        yield return Filter;
        yield return Crop;
    }
}
