using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Storefront.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class SearchController(ISender sender) : Controller
{
    public async Task<IActionResult> Index(
        string? q, decimal? minPrice, decimal? maxPrice, Guid? brandId,
        ProductSortOrder sort = ProductSortOrder.Default, int page = 1, int pageSize = ProductListingHelper.DefaultPageSize)
    {
        var result = await GetProductsAsync(q, minPrice, maxPrice, brandId, sort, page, pageSize);

        var allBrands = await sender.Send(new GetBrandsQuery());
        var activeBrands = allBrands.Where(b => b.IsActive).ToList();

        var model = new ProductListingViewModel(
            result.Items, activeBrands, q, minPrice, maxPrice, brandId,
            sort, page, ProductListingHelper.NormalizePageSize(pageSize), result.TotalCount, result.TotalPages);
        return View(model);
    }

    /// <summary>Sonsuz kaydırma (bkz. plan §6) - Index'in kullandığı AYNI sorgu (marka listesi
    /// olmadan), yalnızca `_ProductGrid` partial'ı döner. `infinite-scroll.js` tarafından fetch edilir.</summary>
    public async Task<IActionResult> IndexPartial(
        string? q, decimal? minPrice, decimal? maxPrice, Guid? brandId,
        ProductSortOrder sort = ProductSortOrder.Default, int page = 1, int pageSize = ProductListingHelper.DefaultPageSize)
    {
        var result = await GetProductsAsync(q, minPrice, maxPrice, brandId, sort, page, pageSize);
        return PartialView("_ProductGrid", result.Items);
    }

    private async Task<PagedResult<StorefrontProductListItemDto>> GetProductsAsync(
        string? q, decimal? minPrice, decimal? maxPrice, Guid? brandId, ProductSortOrder sort, int page, int pageSize)
    {
        pageSize = ProductListingHelper.NormalizePageSize(pageSize);
        page = Math.Max(1, page);

        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        return await sender.Send(new GetStorefrontProductsQuery(
            StorefrontLanguage.GetLanguage(HttpContext), CategoryId: null, page, pageSize,
            SearchText: q, MinPriceTry: minPrice, MaxPriceTry: maxPrice, BrandId: brandId, SortBy: sort, CustomerGroupId: customerGroupId));
    }
}
