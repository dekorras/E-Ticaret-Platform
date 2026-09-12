using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Queries;

public sealed record CouponDto(
    Guid Id,
    string Code,
    DiscountType DiscountType,
    decimal DiscountValue,
    DateTime ValidFromUtc,
    DateTime ValidToUtc,
    int? UsageLimit,
    int UsageCount,
    bool IsActive);

public sealed record GetCouponsQuery : IRequest<IReadOnlyCollection<CouponDto>>;

public sealed class GetCouponsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCouponsQuery, IReadOnlyCollection<CouponDto>>
{
    public Task<IReadOnlyCollection<CouponDto>> Handle(GetCouponsQuery request, CancellationToken cancellationToken)
    {
        var coupons = unitOfWork.Repository<Coupon>().Query()
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new CouponDto(c.Id, c.Code, c.DiscountType, c.DiscountValue, c.ValidFromUtc, c.ValidToUtc, c.UsageLimit, c.UsageCount, c.IsActive))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CouponDto>>(coupons);
    }
}
