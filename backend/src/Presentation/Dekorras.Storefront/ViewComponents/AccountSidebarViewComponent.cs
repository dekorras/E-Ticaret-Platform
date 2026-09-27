using System.Security.Claims;
using Dekorras.Application.Customers.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.ViewComponents;

/// <summary>"Hesabım" sol menüsü: ad-soyad baş harfleri, "Duvarımda Dene" tanıtım kutusu ve bölüm başlıkları.
/// Etkin bölüm, sayfanın <c>ViewData["AccountNav"]</c> değeriyle işaretlenir.</summary>
public class AccountSidebarViewComponent(ISender sender) : ViewComponent
{
    public sealed record Model(string FullName, string Initials, string Active);

    public async Task<IViewComponentResult> InvokeAsync(string active)
    {
        var identityUserId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        var profile = identityUserId is null ? null : await sender.Send(new GetMyCustomerProfileQuery(identityUserId));
        var name = profile?.FullName ?? User.Identity?.Name ?? "";
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0], new System.Globalization.CultureInfo("tr-TR"))));
        return View(new Model(name, initials, active));
    }
}
