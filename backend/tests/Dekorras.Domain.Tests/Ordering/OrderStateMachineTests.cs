using Dekorras.Domain.Common;
using Dekorras.Domain.Ordering;

namespace Dekorras.Domain.Tests.Ordering;

public class OrderStateMachineTests
{
    private static Order CreateOrder() => new("SIP-0001", Guid.NewGuid(), OrderSource.Web, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void YeniSiparis_OnayBekliyorDurumundaBaslar()
    {
        var order = CreateOrder();
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
    }

    [Fact]
    public void GecerliGecis_HazirlanmaAsamalarindanKargoyaGecebilir()
    {
        var order = CreateOrder();

        order.TransitionTo(OrderStatus.Preparing);
        order.TransitionTo(OrderStatus.Prepared);
        order.TransitionTo(OrderStatus.Shipped);

        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Equal(4, order.StatusHistory.Count); // başlangıç + 3 geçiş
    }

    [Fact]
    public void GecersizGecis_OnayBekliyorkenDogrudanTamamlandiyaGecemez()
    {
        var order = CreateOrder();

        Assert.Throws<InvalidStateTransitionException>(() => order.TransitionTo(OrderStatus.Completed));
    }

    [Fact]
    public void TamamlandiDurumu_OrderCompletedEventiUretir()
    {
        var order = CreateOrder();
        order.TransitionTo(OrderStatus.Preparing);
        order.TransitionTo(OrderStatus.Prepared);
        order.TransitionTo(OrderStatus.Shipped);
        order.TransitionTo(OrderStatus.Completed);

        var domainEvent = Assert.Single(order.DomainEvents);
        Assert.IsType<OrderCompletedEvent>(domainEvent);
    }

    [Fact]
    public void NihaiDurumdan_HicbirYereGecisYapilamaz()
    {
        var order = CreateOrder();
        order.TransitionTo(OrderStatus.Rejected);

        Assert.Throws<InvalidStateTransitionException>(() => order.TransitionTo(OrderStatus.Preparing));
    }
}
