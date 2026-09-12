using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

/// <summary>Storefront'un ürün fiyatı gösterirken/sepete eklerken müşterinin grubuna özel fiyatı
/// (bkz. `Product.GroupPrices`, plan §2.6 - "ürün fiyatlarını sadece belirli gruplara gösterme
/// opsiyonu") çözümleyebilmesi için tek durak noktası - misafir/kimliği doğrulanmamış istekler
/// için `null` döner (bu durumda çağıran taraf temel fiyata düşer).</summary>
public sealed record GetMyCustomerGroupIdQuery(string? IdentityUserId) : IRequest<Guid?>;

public sealed class GetMyCustomerGroupIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyCustomerGroupIdQuery, Guid?>
{
    public Task<Guid?> Handle(GetMyCustomerGroupIdQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.IdentityUserId))
            return Task.FromResult<Guid?>(null);

        var groupId = unitOfWork.Repository<Customer>().Query()
            .Where(c => c.IdentityUserId == request.IdentityUserId)
            .Select(c => (Guid?)c.CustomerGroupId)
            .FirstOrDefault();

        return Task.FromResult(groupId);
    }
}
