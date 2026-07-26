using System.Data;
using OnlineShop.Domain.Inventory;

namespace OnlineShop.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Write side of stock. Implemented by <c>InventoryRepository</c>.
/// </summary>
public interface IInventoryRepository
{
    Task<InventoryItem?> GetByProductIdAsync(
        Guid tenantId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        InventoryItem item,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves stock with a single conditional UPDATE and reports whether it
    /// succeeded.
    /// </summary>
    /// <remarks>
    /// The availability check lives in the UPDATE's WHERE clause rather than in
    /// a preceding SELECT. Two checkouts racing for the last unit therefore
    /// serialise on the row lock and exactly one of them affects a row; a
    /// read-then-write pair would let both pass the check and oversell.
    /// </remarks>
    Task<bool> TryReserveAsync(
        Guid tenantId,
        Guid productId,
        int quantity,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>Returns reserved stock to the available pool, e.g. on cancellation.</summary>
    Task ReleaseReservationAsync(
        Guid tenantId,
        Guid productId,
        int quantity,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task AdjustOnHandAsync(
        Guid tenantId,
        Guid productId,
        int delta,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}
