using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Storefront.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class CategoryController(ISender sender) : Controller
{
    /// <summary>Kabul kriteri: mevcut sitedeki kategori URL slug yapısı korunmalı - rota doğrudan
    /// slug üzerinden çalışır (bkz. plan §12).</summary>
    public async Task<IActionResult> Index(
        string slug, decimal? minPrice, decimal? maxPrice, Guid? brandId,
        ProductSortOrder sort = ProductSortOrder.Default, int page = 1, int pageSize = ProductListingHelper.DefaultPageSize)
    {
        var languageCode = StorefrontLanguage.GetLanguage(HttpContext);
        var category = await sender.Send(new GetCategoryBySlugQuery(slug, languageCode));
        if (category is null) return NotFound();

        var result = await GetProductsAsync(category.Id, languageCode, minPrice, maxPrice, brandId, sort, page, pageSize);

        var allBrands = await sender.Send(new GetBrandsQuery());
        var activeBrands = allBrands.Where(b => b.IsActive).ToList();

        ViewBag.Category = category;
        var model = new ProductListingViewModel(
            result.Items, activeBrands, SearchText: null, minPrice, maxPrice, brandId,
            sort, page, ProductListingHelper.NormalizePageSize(pageSize), result.TotalCount, result.TotalPages);
        return View(model);
    }

    /// <summary>Sonsuz kaydırma (bkz. plan §6) - Index'in kullandığı AYNI sorgu (marka listesi
    /// olmadan), yalnızca `_ProductGrid` partial'ı döner. `infinite-scroll.js` tarafından fetch edilir.</summary>
    public async Task<IActionResult> IndexPartial(
        string slug, decimal? minPrice, decimal? maxPrice, Guid? brandId,
        ProductSortOrder sort = ProductSortOrder.Default, int page = 1, int pageSize = ProductListingHelper.DefaultPageSize)
    {
        var languageCode = StorefrontLanguage.GetLanguage(HttpContext);
        var category = await sender.Send(new GetCategoryBySlugQuery(slug, languageCode));
        if (category is null) return NotFound();

        var result = await GetProductsAsync(category.Id, languageCode, minPrice, maxPrice, brandId, sort, page, pageSize);
        return PartialView("_ProductGrid", result.Items);
    }

    private async Task<PagedResult<StorefrontProductListItemDto>> GetProductsAsync(
        Guid categoryId, string languageCode, decimal? minPrice, decimal? maxPrice, Guid? brandId,
        ProductSortOrder sort, int page, int pageSize)
    {
        pageSize = ProductListingHelper.NormalizePageSize(pageSize);
        page = Math.Max(1, page);

        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        return await sender.Send(new GetStorefrontProductsQuery(
            languageCode, categoryId, page, pageSize,
            MinPriceTry: minPrice, MaxPriceTry: maxPrice, BrandId: brandId, SortBy: sort, CustomerGroupId: customerGroupId));
    }
}
