using Dekorras.Application.Catalog.Storefront;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.ViewComponents;

public class MainMenuViewComponent(ISender sender) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var menu = await sender.Send(new GetStorefrontMenuQuery(StorefrontLanguage.GetLanguage(HttpContext)));
        return View(menu);
    }
}
