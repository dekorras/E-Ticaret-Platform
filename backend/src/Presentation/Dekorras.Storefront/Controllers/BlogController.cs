using Dekorras.Application.Content.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

public class BlogController(ISender sender) : Controller
{
    public async Task<IActionResult> Index()
    {
        var posts = await sender.Send(new GetPublishedBlogPostsQuery(StorefrontLanguage.GetLanguage(HttpContext)));
        return View(posts);
    }

    public async Task<IActionResult> Details(string slug)
    {
        var post = await sender.Send(new GetBlogPostBySlugQuery(slug, StorefrontLanguage.GetLanguage(HttpContext)));
        return post is null ? NotFound() : View(post);
    }
}
