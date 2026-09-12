using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Dekorras.Infrastructure.Web;

/// <summary>Bkz. plan §11 - "güvenlik başlıkları (CSP, HSTS)". HSTS zaten `UseHsts()` ile ayrı
/// yapılandırılıyor; CSP ise BİLİNÇLİ OLARAK buraya eklenmedi - Admin'in Blazor Server SignalR
/// devresi (WebSocket) ve inline stil kullanımı için doğru bir CSP, geniş bir manuel test olmadan
/// yanlış yapılandırılırsa TÜM etkileşimli butonları sessizce kırabilir (bkz. README). Bunun yerine
/// hiçbir işlevsel riski olmayan, saf ekleme niteliğindeki üç başlık eklendi - Admin/Api/Storefront
/// üçünün de paylaştığı tek durak noktası.</summary>
public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseDekorrasSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            await next();
        });
    }
}
