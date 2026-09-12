using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;
using MediatR;

namespace Dekorras.Application.Integrations.Commands;

/// <summary>`IntegrationProvider.MarkAsDefaultForCategory`/`UnmarkAsDefaultForCategory` Faz 0/1'den
/// beri vardı ama hiçbir yerden çağrılamıyordu/okunmuyordu - bir kategoride (ödeme/kargo) birden
/// fazla aktif sağlayıcı varken checkout'ta hiçbiri ön-seçili gelmiyordu. Aynı anda yalnızca BİR
/// sağlayıcı bir kategoride varsayılan olabilir.</summary>
public sealed record SetIntegrationProviderDefaultCommand(Guid ProviderId) : IRequest<Unit>;

public sealed class SetIntegrationProviderDefaultCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetIntegrationProviderDefaultCommand, Unit>
{
    public async Task<Unit> Handle(SetIntegrationProviderDefaultCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<IntegrationProvider>();
        var target = await repository.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProviderId}' numaralı sağlayıcı bulunamadı.");

        var otherDefaults = repository.Query()
            .Where(p => p.Category == target.Category && p.Id != target.Id && p.IsDefaultForCategory)
            .ToList();
        foreach (var other in otherDefaults) other.UnmarkAsDefaultForCategory();

        target.MarkAsDefaultForCategory();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
