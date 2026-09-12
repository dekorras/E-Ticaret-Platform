using System.Security.Claims;
using Dekorras.Application.Customers.Queries;
using MediatR;

namespace Dekorras.Storefront;

/// <summary>`StorefrontCustomerGroup`'un CustomerId sürümü - yeni oluşturulan bir sepeti (`Cart.
/// CustomerId`) giriş yapmış müşteriye bağlamak için `CartController` tarafından kullanılır.
/// Kimliği doğrulanmamış (misafir) istekler için `null` döner.</summary>
public static class StorefrontCustomerId
{
    public static Task<Guid?> ResolveAsync(ISender sender, ClaimsPrincipal user)
    {
        var identityUserId = user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        return sender.Send(new GetMyCustomerIdQuery(identityUserId));
    }
}
