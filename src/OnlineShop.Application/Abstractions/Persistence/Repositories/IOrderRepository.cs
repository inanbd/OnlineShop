using System.Data;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Write side of ordering. Implemented by <c>OrderRepository</c>.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(
        Guid tenantId,
        Guid orderId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts the order header and all of its line items.</summary>
    Task InsertAsync(
        Order order,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Order order,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertPaymentAsync(
        Payment payment,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertStatusHistoryAsync(
        OrderStatusHistoryEntry entry,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates the next per-shop order number inside the transaction, so two
    /// concurrent checkouts cannot be handed the same one.
    /// </summary>
    Task<string> NextOrderNumberAsync(
        Guid tenantId,
        Guid shopId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}
