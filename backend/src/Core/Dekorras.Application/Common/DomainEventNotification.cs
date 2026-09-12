using Dekorras.Domain.Common;
using MediatR;

namespace Dekorras.Application.Common;

/// <summary>Persistence katmanı SaveChangesAsync sırasında entity'lerin DomainEvents listesini
/// bu zarfa sararak MediatR üzerinden yayınlar; ilgili INotificationHandler'lar Application
/// katmanında tepki verir (ör. OrderCompletedEvent -> fatura taslağı oluşturma).</summary>
public sealed class DomainEventNotification<TDomainEvent> : INotification where TDomainEvent : DomainEvent
{
    public TDomainEvent DomainEvent { get; }

    public DomainEventNotification(TDomainEvent domainEvent) => DomainEvent = domainEvent;
}
