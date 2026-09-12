namespace Dekorras.Domain.Common;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string entity, string from, string to)
        : base($"{entity} durumu '{from}' iken '{to}' durumuna geçemez.") { }
}
