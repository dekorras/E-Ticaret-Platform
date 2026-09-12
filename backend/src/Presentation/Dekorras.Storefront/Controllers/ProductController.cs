using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Application.Customers.Commands;
using Dekorras.Application.Customers.Queries;
using Dekorras.Storefront.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class ProductController(ISender sender, UserManager<IdentityUser> userManager) : Controller
{
    /// <summary>Kabul kriteri: mevcut sitedeki ürün URL slug yapısı korunmalı (bkz. plan §12).</summary>
    public async Task<IActionResult> Details(string slug)
    {
        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        var product = await sender.Send(new GetProductBySlugQuery(slug, StorefrontLanguage.GetLanguage(HttpContext), customerGroupId));
        if (product is null) return NotFound();

        var reviews = await sender.Send(new GetProductReviewsQuery(product.Id, OnlyApproved: true));
        var questions = await sender.Send(new GetProductQuestionsQuery(product.Id, OnlyAnswered: true));
        var isInWishlist = await sender.Send(new IsInWishlistQuery(User.Identity?.IsAuthenticated == true ? userManager.GetUserId(User) : null, product.Id));
        var relatedProducts = await sender.Send(new GetActiveRelatedProductsQuery(product.Id, StorefrontLanguage.GetLanguage(HttpContext)));

        var model = new ProductDetailViewModel(product, reviews, questions, null, null, isInWishlist, relatedProducts);
        return View(model);
    }

    /// <summary>Ürün kartındaki "Hızlı Bakış" (quickview) - Details'in kullandığı AYNI sorguyu
    /// kullanır (yorum/S-C/benzer ürün olmadan) - bkz. dekorras.com/Journal3 tasarım paritesi
    /// planı. `_ProductQuickview.cshtml` yalnızca bir modal içeriği olarak render edilir.</summary>
    public async Task<IActionResult> Quickview(string slug, string? returnUrl)
    {
        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        var product = await sender.Send(new GetProductBySlugQuery(slug, StorefrontLanguage.GetLanguage(HttpContext), customerGroupId));
        if (product is null) return NotFound();

        // returnUrl gerçek Sepete Ekle sonrası yönlendirme hedefi olmalı (kartın bulunduğu sayfa) -
        // bu action'ın KENDİ URL'i DEĞİL (o yalnızca bir partial döner, tam sayfa olarak açılamaz).
        ViewBag.ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action(nameof(Details), new { slug });

        return PartialView("_ProductQuickview", product);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleWishlist(Guid productId, string slug, bool add)
    {
        var identityUserId = userManager.GetUserId(User)!;
        if (add)
            await sender.Send(new AddToWishlistCommand(identityUserId, productId));
        else
            await sender.Send(new RemoveFromWishlistCommand(identityUserId, productId));

        return RedirectToAction(nameof(Details), new { slug });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReview(Guid productId, string slug, int rating, string comment)
    {
        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        var product = await sender.Send(new GetProductBySlugQuery(slug, StorefrontLanguage.GetLanguage(HttpContext), customerGroupId));
        if (product is null) return NotFound();

        string? reviewError = null;
        try
        {
            await sender.Send(new SubmitProductReviewCommand(productId, userManager.GetUserId(User)!, rating, comment));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FluentValidation.ValidationException)
        {
            reviewError = ex is FluentValidation.ValidationException vex
                ? string.Join(" ", vex.Errors.Select(e => e.ErrorMessage))
                : ex.Message;
        }

        var reviews = await sender.Send(new GetProductReviewsQuery(product.Id, OnlyApproved: true));
        var questions = await sender.Send(new GetProductQuestionsQuery(product.Id, OnlyAnswered: true));
        var isInWishlist = await sender.Send(new IsInWishlistQuery(userManager.GetUserId(User), product.Id));
        return View("Details", new ProductDetailViewModel(product, reviews, questions, reviewError, null, isInWishlist));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitQuestion(Guid productId, string slug, string question)
    {
        var customerGroupId = await StorefrontCustomerGroup.ResolveAsync(sender, User);
        var product = await sender.Send(new GetProductBySlugQuery(slug, StorefrontLanguage.GetLanguage(HttpContext), customerGroupId));
        if (product is null) return NotFound();

        string? questionError = null;
        try
        {
            await sender.Send(new SubmitProductQuestionCommand(productId, userManager.GetUserId(User)!, question));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FluentValidation.ValidationException)
        {
            questionError = ex is FluentValidation.ValidationException vex
                ? string.Join(" ", vex.Errors.Select(e => e.ErrorMessage))
                : ex.Message;
        }

        var reviews = await sender.Send(new GetProductReviewsQuery(product.Id, OnlyApproved: true));
        var questions = await sender.Send(new GetProductQuestionsQuery(product.Id, OnlyAnswered: true));
        var isInWishlist = await sender.Send(new IsInWishlistQuery(userManager.GetUserId(User), product.Id));
        return View("Details", new ProductDetailViewModel(product, reviews, questions, null, questionError, isInWishlist));
    }
}
