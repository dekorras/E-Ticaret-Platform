using System.Globalization;

namespace Dekorras.Domain.WallCovering;

/// <summary>Ölçü birimi dönüşümü ve ölçü kuralları (spec 1.3). İç temsil HER ZAMAN cm, 1 ondalık.</summary>
public static class WallDimensions
{
    public const decimal MinSideCm = 10m;
    public const decimal MaxWidthCm = 2000m;

    public static decimal CmPerUnit(LengthUnit unit) => unit switch
    {
        LengthUnit.Cm => 1m,
        LengthUnit.M => 100m,
        LengthUnit.In => 2.54m,
        LengthUnit.Ft => 30.48m,
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    public static decimal ToCm(decimal value, LengthUnit unit) =>
        Math.Round(value * CmPerUnit(unit), 1, MidpointRounding.AwayFromZero);

    /// <summary>cm'den girilen birime (gösterim için). m ve ft 2, cm ve inç 1 ondalık.</summary>
    public static decimal FromCm(decimal cm, LengthUnit unit)
    {
        var decimals = unit is LengthUnit.M or LengthUnit.Ft ? 2 : 1;
        return Math.Round(cm / CmPerUnit(unit), decimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>"3,00", "3.00", " 3 " gibi girişleri kabul eder. Hem ',' hem '.' ondalık ayırıcıdır;
    /// binlik ayırıcı desteklenmez (VARSAYIM: ölçü girişinde binlik ayırıcı kullanılmaz, "1.500"
    /// 1,5 olarak okunur - m cinsinden "1.500" = 1,5 m zaten doğru yorumdur).</summary>
    public static bool TryParseLength(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var normalized = input.Trim().Replace(',', '.');
        if (normalized.Count(c => c == '.') > 1) return false;

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    public static bool TryParseUnit(string? input, out LengthUnit unit)
    {
        unit = LengthUnit.Cm;
        switch (input?.Trim().ToLowerInvariant())
        {
            case "cm": unit = LengthUnit.Cm; return true;
            case "m": unit = LengthUnit.M; return true;
            case "in" or "inch" or "inç": unit = LengthUnit.In; return true;
            case "ft" or "feet": unit = LengthUnit.Ft; return true;
            default: return false;
        }
    }

    public static string UnitCode(LengthUnit unit) => unit switch
    {
        LengthUnit.Cm => "cm",
        LengthUnit.M => "m",
        LengthUnit.In => "in",
        LengthUnit.Ft => "ft",
        _ => "cm"
    };

    /// <summary>Ölçüyü doğrular, Türkçe hata mesajlarını alan adına göre döner (boşsa geçerli).</summary>
    public static IReadOnlyDictionary<string, string> Validate(decimal widthCm, decimal heightCm, decimal maxHeightCm)
    {
        var errors = new Dictionary<string, string>();

        if (widthCm < MinSideCm) errors["width"] = $"En en az {MinSideCm:0} cm olmalıdır.";
        else if (widthCm > MaxWidthCm) errors["width"] = $"En en fazla {MaxWidthCm:0} cm olabilir.";

        if (heightCm < MinSideCm) errors["height"] = $"Boy en az {MinSideCm:0} cm olmalıdır.";
        else if (heightCm > maxHeightCm) errors["height"] = $"Seçilen malzemede boy en fazla {maxHeightCm:0} cm olabilir.";

        return errors;
    }
}
