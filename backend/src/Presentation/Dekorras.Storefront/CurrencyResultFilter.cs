using Dekorras.Application.Localization.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Dekorras.Storefront;

/// <summary>Her sayfa render'ından ÖNCE, seçilen görüntüleme para biriminin (`StorefrontCurrency`
/// çerezi) TRY'ye göre GÜNCEL kurunu bir kez çözüp `ViewData`'ya yazar - view'lardaki `Html.Money(...)`
/// çağrıları (senkron, I/O YOK) bunu okur. Bu ayrım BİLİNÇLİ: gerçek bir kur servisi (TCMB/3. parti
/// API) ileride `IExchangeRateProvider`'ın yerini alırsa bile, bu async çözümleme HER ZAMAN view
/// render'ından önce (burada) olur - view'ların kendisi hiçbir zaman senkron-üzerinden-asenkron
/// çağrı yapmaz.</summary>
public sealed class CurrencyResultFilter(ISender sender) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Controller is Controller controller && context.Result is ViewResult or PartialViewResult)
        {
            var currencyCode = StorefrontCurrency.GetCurrencyCode(context.HttpContext);
            var conversion = await sender.Send(new GetCurrencyConversionQuery(currencyCode));

            controller.ViewData["CurrencyCode"] = conversion.Code;
            controller.ViewData["CurrencySymbol"] = conversion.Symbol;
            controller.ViewData["CurrencyRate"] = conversion.RateFromBase;
            controller.ViewData["CurrencyDecimalDigits"] = conversion.DecimalDigits;
        }

        await next();
    }
}
