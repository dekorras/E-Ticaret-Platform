using System.Globalization;
using System.Text;
using Dekorras.Application.Content.Models;

namespace Dekorras.Application.Content.Helpers;

/// <summary>
/// Saf statik yardımcı - BannerNodeSettings'ten Bootstrap grid sınıflarını ve satır-içi CSS custom
/// property string'ini üretir. DB/HTTP bağımlılığı yok, bu yüzden birim testi ile doğrudan
/// test edilebilir (bkz. plan §9.6).
/// </summary>
public static class BannerCssBuilder
{
    /// <summary>ör. "col-12 col-md-6 col-lg-4 offset-lg-1 order-md-2"</summary>
    public static string BuildColumnClasses(BannerNodeSettings settings)
    {
        var classes = new List<string> { "col-" + (settings.ColXs ?? 12) };

        AppendIfSet(classes, "col-sm-", settings.ColSm);
        AppendIfSet(classes, "col-md-", settings.ColMd);
        AppendIfSet(classes, "col-lg-", settings.ColLg);
        AppendIfSet(classes, "col-xl-", settings.ColXl);
        AppendIfSet(classes, "col-xxl-", settings.ColXxl);

        if (settings.Offset is > 0) classes.Add("offset-" + settings.Offset);
        if (settings.Order is not null) classes.Add("order-" + settings.Order);
        if (settings.Hidden) classes.Add("d-none");

        return string.Join(' ', classes);
    }

    private static void AppendIfSet(List<string> classes, string prefix, int? value)
    {
        if (value is not null) classes.Add(prefix + value);
    }

    /// <summary>ör. "background-color:#fff;color:#111;padding:40px 20px;min-height:380px;border-radius:8px;text-align:center;"</summary>
    public static string BuildInlineStyle(BannerNodeSettings settings)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(settings.BackgroundColor))
            sb.Append(CultureInfo.InvariantCulture, $"background-color:{settings.BackgroundColor};");
        if (!string.IsNullOrWhiteSpace(settings.BackgroundImageUrl))
            sb.Append(CultureInfo.InvariantCulture, $"background-image:url('{settings.BackgroundImageUrl}');background-size:cover;background-position:center;");
        if (!string.IsNullOrWhiteSpace(settings.TextColor))
            sb.Append(CultureInfo.InvariantCulture, $"color:{settings.TextColor};");
        if (!string.IsNullOrWhiteSpace(settings.PaddingY) || !string.IsNullOrWhiteSpace(settings.PaddingX))
            sb.Append(CultureInfo.InvariantCulture, $"padding:{settings.PaddingY ?? "0"} {settings.PaddingX ?? "0"};");
        if (!string.IsNullOrWhiteSpace(settings.MinHeight))
            sb.Append(CultureInfo.InvariantCulture, $"min-height:{settings.MinHeight};");
        if (!string.IsNullOrWhiteSpace(settings.BorderRadius))
            sb.Append(CultureInfo.InvariantCulture, $"border-radius:{settings.BorderRadius};");
        if (!string.IsNullOrWhiteSpace(settings.TextAlign))
            sb.Append(CultureInfo.InvariantCulture, $"text-align:{settings.TextAlign};");

        return sb.ToString();
    }

    /// <summary>
    /// ContentType.Image için "Alternatif Metin" bindirmesinin satır-içi CSS'i - yalnızca renk ve
    /// yatay hizalama (justify-content, flex tabanlı .dk-bnr-image-text-overlay konteyneri için).
    /// ör. "color:#fff;justify-content:center;text-align:center;"
    /// </summary>
    public static string BuildImageOverlayStyle(BannerContentSettings settings)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(settings.AltTextColor))
            sb.Append(CultureInfo.InvariantCulture, $"color:{settings.AltTextColor};");

        var justifyContent = settings.AltTextAlign switch
        {
            "left" => "flex-start",
            "right" => "flex-end",
            "center" => "center",
            _ => null
        };
        if (justifyContent is not null)
            sb.Append(CultureInfo.InvariantCulture, $"justify-content:{justifyContent};text-align:{settings.AltTextAlign};");

        return sb.ToString();
    }
}
