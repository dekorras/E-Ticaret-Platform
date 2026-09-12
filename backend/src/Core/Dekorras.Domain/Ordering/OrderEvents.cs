using Dekorras.Domain.Common;

namespace Dekorras.Domain.Ordering;

public class OrderCompletedEvent : DomainEvent
{
    public Guid OrderId { get; }
    public Guid CustomerId { get; }
    public decimal TotalAmountTry { get; }

    public OrderCompletedEvent(Guid orderId, Guid customerId, decimal totalAmountTry)
    {
        OrderId = orderId;
        CustomerId = customerId;
        TotalAmountTry = totalAmountTry;
    }
}
