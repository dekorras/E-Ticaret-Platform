namespace Dekorras.Domain.Ordering;

internal static class OrderStatusTransitionRules
{
    // Sipariş durum makinesi: hangi durumdan hangi durumlara geçilebileceğini tanımlar.
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.PendingApproval] = [OrderStatus.Preparing, OrderStatus.Rejected, OrderStatus.Cancelled, OrderStatus.Expired, OrderStatus.Failed, OrderStatus.OnHold],
        [OrderStatus.Preparing] = [OrderStatus.Prepared, OrderStatus.Cancelled, OrderStatus.OnHold],
        [OrderStatus.Prepared] = [OrderStatus.Shipped, OrderStatus.Cancelled, OrderStatus.OnHold],
        [OrderStatus.Shipped] = [OrderStatus.Completed, OrderStatus.Refunded, OrderStatus.Chargeback],
        [OrderStatus.Completed] = [OrderStatus.Refunded, OrderStatus.Chargeback],
        [OrderStatus.Cancelled] = [OrderStatus.CancellationReverted],
        [OrderStatus.CancellationReverted] = [OrderStatus.Preparing],
        [OrderStatus.OnHold] = [OrderStatus.Preparing, OrderStatus.Cancelled, OrderStatus.Voided],
        [OrderStatus.Refunded] = [],
        [OrderStatus.Rejected] = [],
        [OrderStatus.Expired] = [],
        [OrderStatus.Chargeback] = [OrderStatus.Voided],
        [OrderStatus.Failed] = [OrderStatus.Preparing],
        [OrderStatus.Voided] = []
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    public static IReadOnlyCollection<OrderStatus> GetValidNextStates(OrderStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];
}
