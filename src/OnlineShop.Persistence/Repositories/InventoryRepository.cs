using System.Data;
using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Inventory;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Repositories;

/// <summary>
/// Stock write repository.
/// </summary>
/// <remarks>
/// <code>
/// InventoryRepository -> the caller's transaction -> WriteConnection -> primary
/// </code>
/// </remarks>
internal sealed class InventoryRepository : IInventoryRepository
{
    private const string GetByProductIdSql = """
        SELECT
            i.Id,
            i.TenantId,
            i.ShopId,
            i.ProductId,
            i.QuantityOnHand,
            i.QuantityReserved,
            i.ReorderThreshold,
            i.CreatedAt,
            i.UpdatedAt
        FROM dbo.InventoryItems AS i
        WHERE i.TenantId  = @TenantId
          AND i.ProductId = @ProductId;
        """;

    public async Task<InventoryItem?> GetByProductIdAsync(
        Guid tenantId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<InventoryRow>(
            new CommandDefinition(
                GetByProductIdSql,
                new { TenantId = tenantId, ProductId = productId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    private const string InsertSql = """
        INSERT INTO dbo.InventoryItems
        (
            Id,
            TenantId,
            ShopId,
            ProductId,
            QuantityOnHand,
            QuantityReserved,
            ReorderThreshold,
            CreatedAt,
            UpdatedAt
        )
        VALUES
        (
            @Id,
            @TenantId,
            @ShopId,
            @ProductId,
            @QuantityOnHand,
            @QuantityReserved,
            @ReorderThreshold,
            @CreatedAt,
            @UpdatedAt
        );
        """;

    public async Task InsertAsync(
        InventoryItem item,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        TenantGuard.Require(item.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                new
                {
                    item.Id,
                    item.TenantId,
                    item.ShopId,
                    item.ProductId,
                    item.QuantityOnHand,
                    item.QuantityReserved,
                    item.ReorderThreshold,
                    item.CreatedAt,
                    item.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <remarks>
    /// The availability test lives in the WHERE clause, not in a SELECT that
    /// precedes it. Two checkouts racing for the last unit both take a lock on
    /// the same row and are serialised by the engine, so exactly one of them
    /// sees a row affected. A read-then-write pair would let both read "1
    /// available", both decide to proceed, and oversell.
    /// </remarks>
    private const string TryReserveSql = """
        UPDATE dbo.InventoryItems
        SET
            QuantityReserved = QuantityReserved + @Quantity,
            UpdatedAt        = SYSUTCDATETIME()
        WHERE TenantId  = @TenantId
          AND ProductId = @ProductId
          AND (QuantityOnHand - QuantityReserved) >= @Quantity;
        """;

    public async Task<bool> TryReserveAsync(
        Guid tenantId,
        Guid productId,
        int quantity,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "A reservation must be for at least one unit.");
        }

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                TryReserveSql,
                new { TenantId = tenantId, ProductId = productId, Quantity = quantity },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return affected == 1;
    }

    /// <remarks>
    /// Clamped at zero so a double release can never drive the reserved count
    /// negative and inflate apparent availability.
    /// </remarks>
    private const string ReleaseReservationSql = """
        UPDATE dbo.InventoryItems
        SET
            QuantityReserved = CASE
                                   WHEN QuantityReserved >= @Quantity THEN QuantityReserved - @Quantity
                                   ELSE 0
                               END,
            UpdatedAt        = SYSUTCDATETIME()
        WHERE TenantId  = @TenantId
          AND ProductId = @ProductId;
        """;

    public async Task ReleaseReservationAsync(
        Guid tenantId,
        Guid productId,
        int quantity,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "A release must be for at least one unit.");
        }

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                ReleaseReservationSql,
                new { TenantId = tenantId, ProductId = productId, Quantity = quantity },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string AdjustOnHandSql = """
        UPDATE dbo.InventoryItems
        SET
            QuantityOnHand = QuantityOnHand + @Delta,
            UpdatedAt      = SYSUTCDATETIME()
        WHERE TenantId  = @TenantId
          AND ProductId = @ProductId
          AND QuantityOnHand + @Delta >= 0;
        """;

    public async Task AdjustOnHandAsync(
        Guid tenantId,
        Guid productId,
        int delta,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                AdjustOnHandSql,
                new { TenantId = tenantId, ProductId = productId, Delta = delta },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new DbConcurrencyException(
                $"Stock for product '{productId}' was not adjusted by {delta}. There is no inventory row for it in " +
                "this tenant, or the adjustment would have driven quantity on hand below zero.");
        }
    }

    /// <summary>Row shape of <see cref="GetByProductIdSql"/>.</summary>
    private sealed class InventoryRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid ShopId { get; init; }

        public Guid ProductId { get; init; }

        public int QuantityOnHand { get; init; }

        public int QuantityReserved { get; init; }

        public int ReorderThreshold { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public InventoryItem ToDomain()
        {
            return InventoryItem.Restore(
                id: Id,
                tenantId: TenantId,
                shopId: ShopId,
                productId: ProductId,
                quantityOnHand: QuantityOnHand,
                quantityReserved: QuantityReserved,
                reorderThreshold: ReorderThreshold,
                createdAt: CreatedAt,
                updatedAt: UpdatedAt);
        }
    }
}
