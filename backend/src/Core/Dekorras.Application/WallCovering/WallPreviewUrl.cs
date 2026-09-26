using Dekorras.Domain.WallCovering;

namespace Dekorras.Application.WallCovering;

/// <summary>"Duvarında Gör" deep link sözleşmesi (spec 1.6.6-B). Tüm bağlantılar (Tag Helper, e-posta,
/// GET /api/v1/wall-preview/link) buradan üretilir.</summary>
public static class WallPreviewUrl
{
    public const string Path = "/duvarinda-gor";

    public static string Build(string productSlug, WallConfiguration? configuration = null, Guid? sceneId = null, string? align = null,
        string? returnUrl = null, string? source = null, string? baseUrl = null)
    {
        var query = new List<KeyValuePair<string, string>> { new("product", productSlug) };
        if (sceneId is Guid s) query.Add(new("scene", s.ToString("N")));
        if (configuration is not null) query.AddRange(WallConfigurationInput.ToQuery(configuration));
        if (align is "left" or "right") query.Add(new("align", align));
        if (SafeReturnUrl(returnUrl) is { } r) query.Add(new("return", r));
        if (!string.IsNullOrEmpty(source)) query.Add(new("src", source));

        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{baseUrl?.TrimEnd('/')}{Path}?{qs}";
    }

    /// <summary>Açık yönlendirme koruması (ASP.NET Core Url.IsLocalUrl ile aynı kural): yalnızca "/" ile
    /// başlayan site içi yol; protokol-göreli "//host" ve "/\host" biçimleri ile kontrol karakterleri reddedilir.</summary>
    public static string? SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return null;
        var r = returnUrl.Trim();
        if (r.Length > 1000 || r[0] != '/') return null;
        if (r.Length > 1 && (r[1] == '/' || r[1] == '\\')) return null;
        return r.Any(char.IsControl) ? null : r;
    }
}
