using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Queries;

public sealed record GiftVoucherDto(Guid Id, string Code, decimal AmountTry, decimal RemainingBalanceTry, bool IsActive);

public sealed record GetGiftVouchersQuery : IRequest<IReadOnlyCollection<GiftVoucherDto>>;

public sealed class GetGiftVouchersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetGiftVouchersQuery, IReadOnlyCollection<GiftVoucherDto>>
{
    public Task<IReadOnlyCollection<GiftVoucherDto>> Handle(GetGiftVouchersQuery request, CancellationToken cancellationToken)
    {
        var vouchers = unitOfWork.Repository<GiftVoucher>().Query()
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(v => new GiftVoucherDto(v.Id, v.Code, v.AmountTry, v.RemainingBalanceTry, v.IsActive))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<GiftVoucherDto>>(vouchers);
    }
}
