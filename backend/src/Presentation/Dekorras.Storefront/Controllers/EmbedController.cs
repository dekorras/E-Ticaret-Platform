using Dekorras.Application.WallCovering;
using Dekorras.Infrastructure.Web;
using Dekorras.Storefront.WallCovering;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Storefront.Controllers;

/// <summary>Başka platformdaki siteler için gömülebilir "Duvarında Gör" (spec 1.6.6-D). Script
/// (/embed/duvarinda-gor.js) bu sayfayı iframe modal içinde açar. Güvenlik: yalnızca aktif EmbedClient
/// anahtarı, `origin` parametresi istemcinin izinli originlerinden biri olmalı, yanıt CSP
/// frame-ancestors ile YALNIZCA o istemcinin originlerine çerçevelenebilir, her açılış günlük kotadan düşer.</summary>
public class EmbedController(ISender sender) : Controller
{
    [HttpGet("/embed/duvarinda-gor")]
    public async Task<IActionResult> Index(string? key, string? origin, string? product, string? image, string? type, string? scene, string? align)
    {
        var client = string.IsNullOrWhiteSpace(key) ? null : await sender.Send(new GetEmbedClientQuery(key, origin));
        if (client is null || string.IsNullOrWhiteSpace(origin))
            return EmbedError("Bu site için \"Duvarında Gör\" etkin değil (geçersiz anahtar veya izin verilmeyen alan adı).", StatusCodes.Status403Forbidden);

        // Bu yanıt yalnızca istemcinin izinli originleri tarafından çerçevelenebilir.
        HttpContext.Items[SecurityHeadersExtensions.FrameAncestorsItemKey] = string.Join(" ", client.Origins);

        if (!await sender.Send(new ConsumeEmbedQuotaCommand(client.PublicKey)))
            return EmbedError("Günlük kullanım sınırına ulaşıldı. Lütfen daha sonra tekrar deneyin.", StatusCodes.Status429TooManyRequests);

        WallProductDetailDto? detail = null;
        var external = false;
        if (!string.IsNullOrWhiteSpace(product))
            detail = await sender.Send(new GetWallProductDetailQuery(product));

        if (detail is null && !string.IsNullOrWhiteSpace(image))
        {
            try
            {
                var ext = await sender.Send(new RegisterExternalImageCommand(client.PublicKey, image));
                detail = ExternalDetail(ext, type);
                external = true;
            }
            catch (ExternalImageRejectedException ex)
            {
                return EmbedError(ex.Message, StatusCodes.Status400BadRequest);
            }
        }
        if (detail is null) return EmbedError("Poster bulunamadı.", StatusCodes.Status404NotFound);

        var result = await WallVisualizerModelFactory.CreateAsync(sender, HttpContext, detail, scene, align, null, "embed", false, origin.TrimEnd('/'), external);
        return result.Model is null ? EmbedError(result.Error ?? "Görüntüleyici açılamadı.", StatusCodes.Status404NotFound) : View("~/Views/WallPreview/Index.cshtml", result.Model);
    }

    /// <summary>Sistemde kayıtlı olmayan harici poster için görüntüleyicinin beklediği ürün şekli.
    /// Fiyat/sepet bu sistemde değil, ana sitenin kendi sepetinde işlenir (wallpreview:add-to-cart).</summary>
    private static WallProductDetailDto ExternalDetail(ExternalImageDto ext, string? type)
    {
        var aspect = ext.HeightPx > 0 ? Math.Round((decimal)ext.WidthPx / ext.HeightPx, 4) : 0m;
        return new WallProductDetailDto(ext.Id, $"harici-{ext.Id:N}", "Poster", null, ext.PreviewUrl, ext.ThumbUrl, null,
            ext.WidthPx, ext.HeightPx, aspect, type?.ToLowerInvariant() == "pattern" ? "Pattern" : "Mural", null, null, "Straight", [], [], 0m, 0m, null, null);
    }

    private IActionResult EmbedError(string message, int status)
    {
        Response.StatusCode = status;
        return View("~/Views/WallPreview/EmbedError.cshtml", message);
    }
}
