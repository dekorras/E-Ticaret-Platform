using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Dekorras.Infrastructure.Web;

/// <summary>Bkz. plan §11 - "güvenlik başlıkları (CSP, HSTS)". HSTS zaten `UseHsts()` ile ayrı
/// yapılandırılıyor; tam bir CSP ise BİLİNÇLİ OLARAK buraya eklenmedi - Admin'in Blazor Server SignalR
/// devresi (WebSocket) ve inline stil kullanımı için doğru bir CSP, geniş bir manuel test olmadan
/// yanlış yapılandırılırsa TÜM etkileşimli butonları sessizce kırabilir (bkz. README). Yalnızca
/// çerçeveleme kısıtı CSP ile verilir (`frame-ancestors`), diğer yönergeler eklenmez.
///
/// Çerçeveleme: varsayılan olarak yalnızca AYNI site (spec 1.6.6-D - "diğer sayfalarda frame-ancestors
/// 'self'") - "Duvarında Gör" görüntüleyicisi aynı sitede modal iframe içinde açılır. Gömülebilir widget
/// sayfaları (/embed/*) istek başına <see cref="FrameAncestorsItemKey"/> ile izinli originleri belirler;
/// bu durumda X-Frame-Options gönderilmez (ALLOW-FROM desteklenmediği için CSP tek kaynaktır).</summary>
public static class SecurityHeadersExtensions
{
    /// <summary>HttpContext.Items anahtarı: değer, boşlukla ayrılmış izinli origin listesi (ör. "https://a.com https://b.com").</summary>
    public const string FrameAncestorsItemKey = "Dekorras.FrameAncestors";

    public static IApplicationBuilder UseDekorrasSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (context.Items.TryGetValue(FrameAncestorsItemKey, out var value) && value is string origins && origins.Length > 0)
                {
                    headers["Content-Security-Policy"] = $"frame-ancestors 'self' {origins}";
                    headers.Remove("X-Frame-Options");
                }
                else
                {
                    headers["X-Frame-Options"] = "SAMEORIGIN";
                    headers["Content-Security-Policy"] = "frame-ancestors 'self'";
                }
                return Task.CompletedTask;
            });

            await next();
        });
    }
}
