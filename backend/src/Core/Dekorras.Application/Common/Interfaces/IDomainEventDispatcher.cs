using Dekorras.Domain.Common;

namespace Dekorras.Application.Common.Interfaces;

/// <summary>Persistence katmanındaki DbContext, SaveChangesAsync başarılı olduktan sonra
/// değişen entity'lerin biriktirdiği domain event'leri bu servis üzerinden yayınlar.</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAndClearEventsAsync(IEnumerable<BaseEntity> entitiesWithEvents, CancellationToken cancellationToken);
}
