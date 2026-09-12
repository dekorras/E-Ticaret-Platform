using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Dekorras.Storefront;

/// <summary>TRY tutarını `CurrencyResultFilter`'ın önceden `ViewData`'ya yazdığı seçili görüntüleme
/// para birimine çevirip biçimlendirir. Yalnızca aritmetik/biçimlendirme - I/O YOK, bu yüzden
/// senkron kalabilir (bkz. `CurrencyResultFilter` dokümantasyonu).</summary>
public static class MoneyHtmlHelperExtensions
{
    public static IHtmlContent Money(this IHtmlHelper html, decimal tryAmount) => new HtmlString(html.MoneyText(tryAmount));

    /// <summary>`Money`'nin düz metin hâli - `&lt;option&gt;` etiketi gibi HTML işaretlemesinin
    /// GEÇERSİZ olduğu bağlamlarda (ör. bir dropdown seçeneğinin metni) kullanılır.</summary>
    public static string MoneyText(this IHtmlHelper html, decimal tryAmount)
    {
        var rate = html.ViewData["CurrencyRate"] as decimal? ?? 1m;
        var symbol = html.ViewData["CurrencySymbol"] as string ?? "₺";
        var decimalDigits = html.ViewData["CurrencyDecimalDigits"] as int? ?? 2;

        var converted = tryAmount * rate;
        return $"{converted.ToString("N" + decimalDigits, CultureInfo.InvariantCulture)} {symbol}";
    }
}
