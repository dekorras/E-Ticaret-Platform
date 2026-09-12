namespace Dekorras.Storefront;

/// <summary>Ürün karşılaştırma listesi (bkz. plan §2.1 - "ürün listeleme sayfası: ... ürün
/// karşılaştırma listesi") bir hesaba/oturuma değil, tarayıcı çerezine bağlıdır - `StorefrontCurrency`/
/// `StorefrontLanguage` ile AYNI desen. Bir hesap/DB kaydı GEREKMEZ çünkü karşılaştırma listesi
/// tamamen geçici bir gözatma yardımcısıdır (sepet/favoriler gibi kalıcı bir niyet taşımaz).</summary>
public static class StorefrontCompareList
{
    private const string CookieName = "dekorras_compare";
    public const int MaxItems = 4;

    public static IReadOnlyList<Guid> GetProductIds(HttpContext httpContext)
    {
        var raw = httpContext.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
    }

    public static void AddProduct(HttpContext httpContext, Guid productId)
    {
        var ids = GetProductIds(httpContext).ToList();
        if (ids.Contains(productId)) return;
        if (ids.Count >= MaxItems) ids.RemoveAt(0); // en eski karşılaştırma listeden düşer
        ids.Add(productId);
        Save(httpContext, ids);
    }

    public static void RemoveProduct(HttpContext httpContext, Guid productId)
    {
        var ids = GetProductIds(httpContext).Where(id => id != productId).ToList();
        Save(httpContext, ids);
    }

    public static void Clear(HttpContext httpContext) => Save(httpContext, []);

    private static void Save(HttpContext httpContext, IReadOnlyList<Guid> ids)
    {
        httpContext.Response.Cookies.Append(CookieName, string.Join(',', ids), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            IsEssential = true
        });
    }
}
