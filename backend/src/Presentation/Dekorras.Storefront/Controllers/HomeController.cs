using System.Diagnostics;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Application.Content.Queries;
using Dekorras.Storefront.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class HomeController(ISender sender) : Controller
{
    public async Task<IActionResult> Index()
    {
        var language = StorefrontLanguage.GetLanguage(HttpContext);
        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);

        ViewBag.Banners = await sender.Send(new GetActiveBannersQuery());
        // BannerZoneViewComponent kendi içinde boşsa hiçbir şey render etmiyor ("Empty" view) -
        // view'ın klasik hero carousel'e ne zaman geri döneceğini bilmesi için burada ayrıca sorulur.
        var bannerZone = await sender.Send(new GetBannerZoneTreeQuery("home-main"));
        ViewBag.HasBannerZoneContent = bannerZone is not null && bannerZone.Roots.Count > 0;

        // Anasayfa artık TÜM ürünleri filtresiz göstermek YERİNE, admin'in "anasayfada göster"
        // olarak işaretlediği (SetCategoryFeaturedOnHomepageCommand) kategorilere özel "blok"lar
        // render eder - anasayfanın yapısı modülerleştirildi. Her blok, mevcut ProductWidget banner
        // içeriğiyle AYNI GetStorefrontProductsQuery çağrısını yeniden kullanır, yeni bir ürün
        // sorgu mantığı yazılmadı. Hiç ürünü olmayan bir kategori (ör. henüz yayınlanmış ürünü yok)
        // boş bir blok olarak GÖRÜNMEZ.
        var featuredCategories = await sender.Send(new GetFeaturedHomepageCategoriesQuery(language));

        var blocks = new List<HomeCategoryBlock>();
        foreach (var category in featuredCategories)
        {
            var categoryProducts = await sender.Send(new GetStorefrontProductsQuery(
                language, category.Id, Page: 1, PageSize: 8, CustomerGroupId: customerGroupId));
            if (categoryProducts.Items.Count > 0)
                blocks.Add(new HomeCategoryBlock(category.Name, category.Slug, categoryProducts.Items));
        }

        return View(blocks);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
