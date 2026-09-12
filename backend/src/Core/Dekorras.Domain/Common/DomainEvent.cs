namespace Dekorras.Domain.Common;

public abstract class DomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
