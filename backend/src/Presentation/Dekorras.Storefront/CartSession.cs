namespace Dekorras.Storefront;

/// <summary>Misafir sepetini tarayıcılar arası oturumlarda tanımlamak için kalıcı, HttpOnly bir
/// çerez kullanılır - üyelik gerektirmez (bkz. plan §2.2 "misafir alışverişi").</summary>
public static class CartSession
{
    private const string CookieName = "dekorras_cart";

    public static string GetOrCreateSessionKey(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var existing) && !string.IsNullOrWhiteSpace(existing))
            return existing;

        var sessionKey = Guid.NewGuid().ToString("N");
        httpContext.Response.Cookies.Append(CookieName, sessionKey, new CookieOptions
        {
            HttpOnly = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            IsEssential = true
        });

        return sessionKey;
    }

    /// <summary>Çıkış yapıldığında çağrılır - aksi halde bir sonraki kullanıcı (paylaşılan bir
    /// cihazda) ÖNCEKİ müşterinin `CustomerId`'ye bağlanmış sepetini devralırdı (bkz.
    /// `MergeGuestCartIntoCustomerCommand`'daki "başka bir müşteriye ait sepete dokunma" koruması -
    /// bu koruma yalnızca çıkış yapılmadan tarayıcı kapatılan istisnai durum için bir güvenlik ağı,
    /// normal akışta çerezin burada temizlenmesi bu duruma hiç düşürmez).</summary>
    public static void ClearSessionKey(HttpContext httpContext) => httpContext.Response.Cookies.Delete(CookieName);
}
