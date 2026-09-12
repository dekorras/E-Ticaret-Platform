using Dekorras.Application.Content.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.ViewComponents;

/// <summary>
/// Dinamik anasayfa/bölge banner ağacını (bkz. BannerZone/BannerNode/BannerContent) render eder.
/// Bölge yoksa veya kök düğümü yoksa "Empty" view'ı döner (boş) - çağıran taraf (ör. Home/Index)
/// bunun yerine kendi fallback'ini (klasik hero carousel) göstermekten sorumludur.
/// </summary>
public class BannerZoneViewComponent(ISender sender) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string zoneKey)
    {
        var zone = await sender.Send(new GetBannerZoneTreeQuery(zoneKey));
        if (zone is null || zone.Roots.Count == 0) return View("Empty");
        return View(zone);
    }
}
