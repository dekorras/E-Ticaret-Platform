using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Marketing;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

public sealed record SetCampaignActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetCampaignActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCampaignActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetCampaignActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Campaign>();
        var campaign = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı kampanya bulunamadı.");

        // Update(campaign) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.IsActive) campaign.Activate(); else campaign.Deactivate();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
