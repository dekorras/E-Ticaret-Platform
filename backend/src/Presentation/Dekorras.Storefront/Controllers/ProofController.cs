using Dekorras.Application.WallCovering;
using Dekorras.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

/// <summary>Müşterinin onay önizlemesi sayfası (spec 1.7): e-postadaki bağlantı (tahmin edilemez token)
/// giriş gerektirmeden açılır; "Onayla" veya "Revizyon iste" (açıklama zorunlu).</summary>
public class ProofController(ISender sender) : Controller
{
    [HttpGet("/siparis-onay/{token}")]
    public async Task<IActionResult> Index(string token)
    {
        var proof = await sender.Send(new GetProofByTokenQuery(token));
        return proof is null ? NotFound() : View(proof);
    }

    [HttpPost("/siparis-onay/{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(string token, string decision, string? note)
    {
        try
        {
            await sender.Send(new RespondToProofCommand(token, decision == "approve", note));
            TempData["ProofMessage"] = decision == "approve"
                ? "Teşekkürler! Tasarımınız onaylandı, siparişiniz üretime alınacak."
                : "Revizyon talebiniz alındı. Ekibimiz en kısa sürede sizinle iletişime geçecek.";
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (DomainException ex) { TempData["ProofError"] = ex.Message; }
        return Redirect($"/siparis-onay/{token}");
    }
}
