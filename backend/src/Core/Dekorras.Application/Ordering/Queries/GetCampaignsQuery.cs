using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Queries;

public sealed record CampaignListItemDto(
    Guid Id,
    string Name,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MinCartTotalTry,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? UsageLimit,
    int UsageCount,
    bool IsActive);

public sealed record GetCampaignsQuery : IRequest<IReadOnlyCollection<CampaignListItemDto>>;

public sealed class GetCampaignsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCampaignsQuery, IReadOnlyCollection<CampaignListItemDto>>
{
    public Task<IReadOnlyCollection<CampaignListItemDto>> Handle(GetCampaignsQuery request, CancellationToken cancellationToken)
    {
        // MinCartTotal kural değeri metin (RuleValueJson) olarak saklanıyor - decimal'e çevirme
        // (LINQ-to-Entities'e çevrilemeyen decimal.Parse) SQL'den DÖNDÜKTEN SONRA, bellekte yapılır.
        var raw = unitOfWork.Repository<Campaign>().Query()
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.DiscountType,
                c.DiscountValue,
                MinCartTotalRaw = c.Rules.Where(r => r.RuleType == "MinCartTotal").Select(r => r.RuleValueJson).FirstOrDefault(),
                c.StartsAtUtc,
                c.EndsAtUtc,
                c.UsageLimit,
                c.UsageCount,
                c.IsActive
            })
            .ToList();

        var campaigns = raw
            .Select(x => new CampaignListItemDto(
                x.Id, x.Name, x.DiscountType, x.DiscountValue,
                decimal.TryParse(x.MinCartTotalRaw, System.Globalization.CultureInfo.InvariantCulture, out var minTotal) ? minTotal : null,
                x.StartsAtUtc, x.EndsAtUtc, x.UsageLimit, x.UsageCount, x.IsActive))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CampaignListItemDto>>(campaigns);
    }
}
