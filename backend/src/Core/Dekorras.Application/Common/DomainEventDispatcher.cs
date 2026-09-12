using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Common;
using MediatR;

namespace Dekorras.Application.Common;

public sealed class DomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    public async Task DispatchAndClearEventsAsync(IEnumerable<BaseEntity> entitiesWithEvents, CancellationToken cancellationToken)
    {
        foreach (var entity in entitiesWithEvents)
        {
            var events = entity.DomainEvents.ToList();
            entity.ClearDomainEvents();

            foreach (var domainEvent in events)
            {
                var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
                var notification = (INotification)Activator.CreateInstance(notificationType, domainEvent)!;
                await publisher.Publish(notification, cancellationToken);
            }
        }
    }
}
