using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

/// <summary>Kupondan farkı: müşteri kod girmez, <paramref name="MinCartTotalTry"/> (varsa) sağlanan
/// her sepete checkout ANINDA otomatik uygulanır - bkz. Domain.Marketing.Campaign belgesi.</summary>
public sealed record CreateCampaignCommand(
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DiscountType DiscountType,
    decimal DiscountValue,
    int? UsageLimit,
    decimal? MinCartTotalTry) : IRequest<Guid>;

public sealed class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.EndsAtUtc).GreaterThan(x => x.StartsAtUtc).WithMessage("Bitiş tarihi başlangıçtan sonra olmalıdır.");
    }
}

public sealed class CreateCampaignCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateCampaignCommand, Guid>
{
    public async Task<Guid> Handle(CreateCampaignCommand request, CancellationToken cancellationToken)
    {
        var campaign = new Campaign(request.Name, request.StartsAtUtc, request.EndsAtUtc, request.DiscountType, request.DiscountValue, request.UsageLimit);

        if (request.MinCartTotalTry is decimal minCartTotal)
            campaign.AddRule("MinCartTotal", minCartTotal.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await unitOfWork.Repository<Campaign>().AddAsync(campaign, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return campaign.Id;
    }
}
