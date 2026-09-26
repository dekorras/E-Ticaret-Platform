using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Dekorras.Storefront;

/// <summary>TRY tutarını `CurrencyResultFilter`'ın önceden `ViewData`'ya yazdığı seçili görüntüleme
/// para birimine çevirip biçimlendirir. Yalnızca aritmetik/biçimlendirme - I/O YOK, bu yüzden
/// senkron kalabilir (bkz. `CurrencyResultFilter` dokümantasyonu).</summary>
public static class MoneyHtmlHelperExtensions
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    public static IHtmlContent Money(this IHtmlHelper html, decimal tryAmount) => new HtmlString(html.MoneyText(tryAmount));

    /// <summary>`Money`'nin düz metin hâli - `&lt;option&gt;` etiketi gibi HTML işaretlemesinin
    /// GEÇERSİZ olduğu bağlamlarda (ör. bir dropdown seçeneğinin metni) kullanılır.</summary>
    public static string MoneyText(this IHtmlHelper html, decimal tryAmount)
    {
        var rate = html.ViewData["CurrencyRate"] as decimal? ?? 1m;
        var symbol = html.ViewData["CurrencySymbol"] as string ?? "₺";
        var decimalDigits = html.ViewData["CurrencyDecimalDigits"] as int? ?? 2;

        var converted = tryAmount * rate;
        // TRY daima tr-TR biçiminde gösterilir ("1.499,00 ₺"); diğer para birimleri eskisi gibi (1,499.00).
        var culture = symbol == "₺" ? TurkishCulture : CultureInfo.InvariantCulture;
        return $"{converted.ToString("N" + decimalDigits, culture)} {symbol}";
    }
}
