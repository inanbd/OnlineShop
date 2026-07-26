using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Ordering;

/// <summary>
/// An append-only audit row describing one order status transition.
/// </summary>
public sealed class OrderStatusHistoryEntry : ITenantOwned
{
    private OrderStatusHistoryEntry(
        Guid id,
        Guid tenantId,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string? reason,
        Guid? changedByUserId,
        DateTime occurredAt)
    {
        Id = id;
        TenantId = tenantId;
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        ChangedByUserId = changedByUserId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid OrderId { get; }

    /// <summary>Null for the row recorded when the order is first created.</summary>
    public OrderStatus? FromStatus { get; }

    public OrderStatus ToStatus { get; }

    public string? Reason { get; }

    public Guid? ChangedByUserId { get; }

    public DateTime OccurredAt { get; }

    public static OrderStatusHistoryEntry Record(
        Guid tenantId,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string? reason,
        Guid? changedByUserId,
        DateTime utcNow)
    {
        return new OrderStatusHistoryEntry(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            orderId: Guard.AgainstEmpty(orderId),
            fromStatus: fromStatus,
            toStatus: toStatus,
            reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            changedByUserId: changedByUserId,
            occurredAt: utcNow);
    }

    public static OrderStatusHistoryEntry Restore(
        Guid id,
        Guid tenantId,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string? reason,
        Guid? changedByUserId,
        DateTime occurredAt)
    {
        return new OrderStatusHistoryEntry(
            id,
            tenantId,
            orderId,
            fromStatus,
            toStatus,
            reason,
            changedByUserId,
            occurredAt);
    }
}
