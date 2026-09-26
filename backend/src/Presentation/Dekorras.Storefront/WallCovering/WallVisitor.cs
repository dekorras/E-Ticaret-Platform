using System.Security.Claims;
using System.Security.Cryptography;
using Dekorras.Application.Customers.Queries;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Storefront.WallCovering;

/// <summary>Duvar kağıdı modülünde ziyaretçinin kimliği: üye ise Customer.Id, misafir ise kalıcı
/// HttpOnly çerezdeki rastgele anahtar ("Duvarımda Dene" listesi, kendi oda sahneleri, kota). Misafir
/// favorileri de ayrı bir çerezde tutulur; girişte <see cref="MergeGuestDataAsync"/> ile üyeye taşınır.</summary>
public static class WallVisitor
{
    private const string GuestCookie = "dekorras_wall";
    private const string FavoritesCookie = "dekorras_fav";
    public const int MaxGuestFavorites = 50;

    public static string? GetGuestKey(HttpContext http) =>
        http.Request.Cookies.TryGetValue(GuestCookie, out var key) && key.Length is >= 16 and <= 64 && key.All(char.IsAsciiLetterOrDigit) ? key : null;

    public static string GetOrCreateGuestKey(HttpContext http)
    {
        if (GetGuestKey(http) is { } existing) return existing;
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        http.Response.Cookies.Append(GuestCookie, key, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(180)
        });
        return key;
    }

    public static async Task<Guid?> GetCustomerIdAsync(HttpContext http, ISender sender) =>
        http.User.Identity?.IsAuthenticated == true ? await StorefrontCustomerId.ResolveAsync(sender, http.User) : null;

    /// <summary>Üyeyse müşteri anahtarı, değilse misafir anahtarı (gerekirse çerez oluşturur).</summary>
    public static async Task<string> GetOwnerKeyAsync(HttpContext http, ISender sender) =>
        await GetCustomerIdAsync(http, sender) is Guid customerId
            ? WallOwner.ForCustomer(customerId)
            : WallOwner.ForGuest(GetOrCreateGuestKey(http));

    public static string? GetIdentityUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public static List<Guid> GetGuestFavorites(HttpContext http) =>
        http.Request.Cookies.TryGetValue(FavoritesCookie, out var raw) && !string.IsNullOrEmpty(raw)
            ? raw.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().Take(MaxGuestFavorites).ToList()
            : [];

    public static void SetGuestFavorites(HttpContext http, IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().Take(MaxGuestFavorites).ToList();
        if (list.Count == 0)
        {
            http.Response.Cookies.Delete(FavoritesCookie);
            return;
        }
        http.Response.Cookies.Append(FavoritesCookie, string.Join('.', list.Select(g => g.ToString("N"))), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(180)
        });
    }

    /// <summary>Giriş/kayıt sonrası: misafir "Duvarımda Dene" listesi ve çerez favorileri üyeye birleştirilir.</summary>
    public static async Task MergeGuestDataAsync(HttpContext http, ISender sender, string identityUserId, Guid customerId)
    {
        if (GetGuestKey(http) is { } guestKey)
        {
            await sender.Send(new MergeTryOnListCommand(WallOwner.ForGuest(guestKey), WallOwner.ForCustomer(customerId)));
            await sender.Send(new MergeUserRoomScenesCommand(WallOwner.ForGuest(guestKey), WallOwner.ForCustomer(customerId)));
        }

        var favorites = GetGuestFavorites(http);
        if (favorites.Count > 0)
        {
            await sender.Send(new MergeGuestFavoritesCommand(identityUserId, favorites));
            http.Response.Cookies.Delete(FavoritesCookie);
        }
    }
}
