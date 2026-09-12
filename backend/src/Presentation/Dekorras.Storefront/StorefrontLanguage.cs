using Dekorras.Application.Common;

namespace Dekorras.Storefront;

/// <summary>Müşterinin seçtiği arayüz/içerik dilini kalıcı bir çerezde tutar (bkz. plan §7 - hedef
/// diller TR/EN/DE/FR/NL/ES/AR, Arapça RTL - tam liste `SupportedLanguages.All`'da). Storefront dil
/// seçicisi burada BİLİNÇLİ olarak yalnızca gerçek .resx UI çevirisi bulunan (TR/EN/AR) diğerlerini
/// gösterir - ürün/kategori İÇERİĞİ `SupportedLanguages.All`'daki HERHANGİ bir dilde olabilir
/// (Admin'de girilebilir), yalnızca sabit arayüz metinleri (nav/buton/vb.) henüz DE/FR/NL/ES için
/// çevrilmedi. Yeni bir .resx eklenince buraya bir satır eklemek yeterli.</summary>
public static class StorefrontLanguage
{
    private const string CookieName = "dekorras_lang";
    public const string DefaultLanguage = "tr";

    public static readonly IReadOnlyList<(string Code, string DisplayName, bool IsRtl)> Supported =
        SupportedLanguages.All.Where(l => l.Code is "tr" or "en" or "ar").ToList();

    public static string GetLanguage(HttpContext httpContext)
    {
        var code = httpContext.Request.Cookies[CookieName];
        return code is not null && Supported.Any(s => s.Code == code) ? code : DefaultLanguage;
    }

    public static void SetLanguage(HttpContext httpContext, string code)
    {
        var normalized = Supported.Any(s => s.Code == code) ? code : DefaultLanguage;
        httpContext.Response.Cookies.Append(CookieName, normalized, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true
        });
    }

    public static bool IsRtl(string code) => Supported.FirstOrDefault(s => s.Code == code).IsRtl;
}
