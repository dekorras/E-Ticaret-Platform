using System.Globalization;
using System.Text.Json.Serialization;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Application.WallCovering;

/// <summary>İstemciden (API gövdesi veya URL query string - spec 1.5 "URL durum senkronizasyonu")
/// gelen ham konfigürasyon. Tüm alanlar metin: hem "3,00" hem "3.00" kabul edilir.
/// <see cref="Parse"/> geçersiz alanları SESSİZCE varsayılana düşürür (URL paylaşımı için);
/// ölçü kurallarının ihlali ise fiyatlama servisinde alan bazlı hataya dönüşür.</summary>
[JsonConverter(typeof(LenientWallConfigurationInputConverter))]
public sealed record WallConfigurationInput(
    string? Material = null,
    string? Unit = null,
    string? Width = null,
    string? Height = null,
    string? WCm = null,
    string? HCm = null,
    string? Fit = null,
    string? Mirror = null,
    string? Filter = null,
    string? Crop = null)
{
    public static WallConfigurationInput FromQuery(Func<string, string?> get) => new(
        get("material"), get("unit"), get("w"), get("h"), get("w_cm"), get("h_cm"),
        get("fit"), get("mirror"), get("filter"), get("crop"));

    /// <param name="defaultMaterialCode">Malzeme verilmemiş/geçersizse kullanılacak kod (ilk aktif malzeme).</param>
    public WallConfiguration Parse(string defaultMaterialCode, decimal defaultWidthCm, decimal defaultHeightCm, IReadOnlyCollection<string>? validMaterialCodes = null)
    {
        var unit = WallDimensions.TryParseUnit(Unit, out var u) ? u : LengthUnit.Cm;

        // Öncelik: cm cinsinden kanonik değer (URL paylaşımı), yoksa girilen birimdeki değer.
        decimal? ReadCm(string? cm, string? inUnit) =>
            WallDimensions.TryParseLength(cm, out var c) ? Math.Round(c, 1, MidpointRounding.AwayFromZero)
            : WallDimensions.TryParseLength(inUnit, out var v) ? WallDimensions.ToCm(v, unit)
            : null;

        var material = !string.IsNullOrWhiteSpace(Material) && (validMaterialCodes is null || validMaterialCodes.Contains(Material.Trim()))
            ? Material.Trim()
            : defaultMaterialCode;

        var fit = Fit?.Trim().ToLowerInvariant() == "stretch" ? FitMode.Stretch : FitMode.Crop;
        var filter = Filter?.Trim().ToLowerInvariant() switch
        {
            "grayscale" => ImageFilter.Grayscale,
            "sepia" => ImageFilter.Sepia,
            _ => ImageFilter.None
        };
        var mirror = Mirror?.Trim().ToLowerInvariant() is "1" or "true" or "on";
        var crop = CropRect.TryParse(Crop, out var c) ? c : null;

        return new WallConfiguration(
            ReadCm(WCm, Width) ?? defaultWidthCm,
            ReadCm(HCm, Height) ?? defaultHeightCm,
            material, unit, fit, mirror, filter, crop);
    }

    /// <summary>Konfigürasyonu URL parametrelerine yazar (spec 1.5 örneğindeki biçim).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ToQuery(WallConfiguration configuration)
    {
        var list = new List<KeyValuePair<string, string>>
        {
            new("material", configuration.MaterialCode),
            new("unit", WallDimensions.UnitCode(configuration.Unit)),
            new("w_cm", configuration.WidthCm.ToString("0.0", CultureInfo.InvariantCulture)),
            new("h_cm", configuration.HeightCm.ToString("0.0", CultureInfo.InvariantCulture)),
            new("fit", configuration.Fit == FitMode.Stretch ? "stretch" : "crop"),
        };
        if (configuration.Mirror) list.Add(new("mirror", "1"));
        if (configuration.Filter != ImageFilter.None) list.Add(new("filter", configuration.Filter.ToString().ToLowerInvariant()));
        if (configuration.Crop is not null) list.Add(new("crop", configuration.Crop.ToQueryValue()));
        return list;
    }
}

/// <summary>API gövdesinde alanların metin, sayı veya bool olarak gelmesini kabul eder
/// (ör. "width": 400 ve "width": "4,00" ikisi de geçerli). Anahtar adları büyük/küçük harfe duyarsız.</summary>
public sealed class LenientWallConfigurationInputConverter : JsonConverter<WallConfigurationInput>
{
    public override WallConfigurationInput? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Null) return null;
        using var doc = System.Text.Json.JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new System.Text.Json.JsonException("configuration bir nesne olmalıdır.");

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            values[p.Name] = p.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => p.Value.GetString(),
                System.Text.Json.JsonValueKind.Number => p.Value.GetRawText(),
                System.Text.Json.JsonValueKind.True => "1",
                System.Text.Json.JsonValueKind.False => "0",
                _ => null
            };
        }

        return WallConfigurationInput.FromQuery(key =>
            values.TryGetValue(key, out var v) ? v
            : key == "w" && values.TryGetValue("width", out var w) ? w
            : key == "h" && values.TryGetValue("height", out var h) ? h
            : null);
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, WallConfigurationInput value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        void W(string name, string? v) { if (v is not null) writer.WriteString(name, v); }
        W("material", value.Material); W("unit", value.Unit); W("width", value.Width); W("height", value.Height);
        W("w_cm", value.WCm); W("h_cm", value.HCm); W("fit", value.Fit); W("mirror", value.Mirror); W("filter", value.Filter); W("crop", value.Crop);
        writer.WriteEndObject();
    }
}
