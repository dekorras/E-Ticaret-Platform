using Dekorras.Application.Content.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class CmsController(ISender sender) : Controller
{
    /// <summary>Gizlilik Politikası, Mesafeli Satış Sözleşmesi, İptal/İade Koşulları gibi statik
    /// kurumsal/yasal sayfalar - yalnızca Aktif bir sayfa döner (bkz. plan §4 "Sayfa Yönetimi").</summary>
    [Route("/sayfa/{slug}")]
    public async Task<IActionResult> Page(string slug)
    {
        var page = await sender.Send(new GetCmsPageBySlugQuery(slug, StorefrontLanguage.GetLanguage(HttpContext)));
        return page is null ? NotFound() : View(page);
    }
}
