using System.Security.Claims;
using Dekorras.Application.Customers.Queries;
using MediatR;

namespace Dekorras.Storefront;

/// <summary>Ürün fiyatı gösterirken/sepete eklerken müşterinin grubuna özel fiyatı (bkz.
/// `Product.GroupPrices`, plan §2.6 - "ürün fiyatlarını sadece belirli gruplara gösterme opsiyonu")
/// çözümlemek için tüm Storefront controller'larının paylaştığı tek durak noktası. Kimliği
/// doğrulanmamış (misafir) istekler için `null` döner.</summary>
public static class StorefrontCustomerGroup
{
    public static Task<Guid?> ResolveAsync(ISender sender, ClaimsPrincipal user)
    {
        var identityUserId = user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        return sender.Send(new GetMyCustomerGroupIdQuery(identityUserId));
    }
}
