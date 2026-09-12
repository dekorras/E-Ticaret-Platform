using Dekorras.Application.Marketing.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dekorras.Storefront.Controllers;

/// <summary>Misafirler için (üyelik gerektirmeyen) bülten kaydı - bkz. `Customer.
/// NewsletterSubscribed`'ın AKSİNE, bu üyelik hesabına bağlı DEĞİLDİR, footer'daki herkese açık
/// formdan kullanılır.</summary>
public class NewsletterController(ISender sender) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Subscribe(string email, string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var confirmUrlTemplate = $"{Request.Scheme}://{Request.Host}/Newsletter/Confirm?id={{0}}";
            await sender.Send(new SubscribeToNewsletterCommand(email.Trim(), confirmUrlTemplate));
        }

        TempData["NewsletterMessage"] = "Aboneliğinizi onaylamak için e-postanızı kontrol edin.";
        return string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)
            ? RedirectToAction("Index", "Home")
            : LocalRedirect(returnUrl);
    }

    public async Task<IActionResult> Confirm(Guid id)
    {
        ViewBag.Success = await sender.Send(new ConfirmNewsletterSubscriptionCommand(id));
        return View();
    }
}
