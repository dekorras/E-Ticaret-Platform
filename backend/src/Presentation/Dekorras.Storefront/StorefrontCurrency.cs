namespace Dekorras.Storefront;

/// <summary>Müşterinin seçtiği GÖRÜNTÜLEME para birimini kalıcı bir çerezde tutar (bkz. plan §7 -
/// "storefront ... güncel kur üzerinden müşterinin seçtiği para biriminde gösterim yapar").
/// `StorefrontLanguage`'daki AYNI çerez deseni. Yalnızca GÖRÜNTÜLEME - sipariş/ödeme HER ZAMAN temel
/// para biriminde (TRY) gerçekleşir, bkz. `CurrencyResultFilter`/`Html.Money` dokümantasyonu.</summary>
public static class StorefrontCurrency
{
    private const string CookieName = "dekorras_currency";
    public const string DefaultCurrencyCode = "TRY";

    public static string GetCurrencyCode(HttpContext httpContext) =>
        httpContext.Request.Cookies[CookieName] ?? DefaultCurrencyCode;

    public static void SetCurrencyCode(HttpContext httpContext, string code)
    {
        httpContext.Response.Cookies.Append(CookieName, code, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true
        });
    }
}
