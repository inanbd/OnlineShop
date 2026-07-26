using OnlineShop.Application.Contracts.Orders;

namespace OnlineShop.Application.Abstractions.Persistence.Queries;

/// <summary>
/// Read model for orders. Implemented by <c>OrderQueries</c> over
/// <c>ReadConnection</c>.
/// </summary>
public interface IOrderQueries
{
    Task<IReadOnlyList<OrderListItemDto>> GetAsync(
        Guid tenantId,
        OrderFilter filter,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        Guid tenantId,
        OrderFilter filter,
        CancellationToken cancellationToken = default);

    /// <param name="consistency">
    /// A shopper redirected to the confirmation page immediately after checkout
    /// passes <see cref="ReadConsistency.Strong"/>, because the replica may not
    /// have the new order yet.
    /// </param>
    Task<OrderDetailsDto?> GetDetailsAsync(
        Guid tenantId,
        Guid orderId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default);
}
