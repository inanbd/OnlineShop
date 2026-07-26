using System.Data;
using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Ordering;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Repositories;

/// <summary>
/// Ordering write repository.
/// </summary>
/// <remarks>
/// <code>
/// OrderRepository -> the caller's transaction -> WriteConnection -> primary
/// </code>
/// Order header, line items, payments and status history are written through
/// the transaction the command handler owns, alongside the inventory and cart
/// changes made by the other repositories in the same flow.
/// </remarks>
internal sealed class OrderRepository : IOrderRepository
{
    private const string GetByIdSql = """
        SELECT
            o.Id,
            o.TenantId,
            o.ShopId,
            o.CustomerId,
            o.OrderNumber,
            o.Status,
            o.CurrencyCode,
            o.PlacedAt,
            o.CreatedAt,
            o.UpdatedAt,
            o.CancelledAt,
            o.CancellationReason
        FROM dbo.Orders AS o
        WHERE o.TenantId = @TenantId
          AND o.Id       = @OrderId;

        SELECT
            oi.Id,
            oi.TenantId,
            oi.OrderId,
            oi.ProductId,
            oi.Sku,
            oi.ProductName,
            oi.Quantity,
            oi.UnitPrice
        FROM dbo.OrderItems AS oi
        WHERE oi.TenantId = @TenantId
          AND oi.OrderId  = @OrderId
        ORDER BY oi.Id ASC;
        """;

    public async Task<Order?> GetByIdAsync(
        Guid tenantId,
        Guid orderId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(
                GetByIdSql,
                new { TenantId = tenantId, OrderId = orderId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var header = await results.ReadSingleOrDefaultAsync<OrderRow>().ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var items = (await results.ReadAsync<OrderItemRow>().ConfigureAwait(false))
            .Select(row => row.ToDomain())
            .ToList();

        return header.ToDomain(items);
    }

    private const string InsertOrderSql = """
        INSERT INTO dbo.Orders
        (
            Id,
            TenantId,
            ShopId,
            CustomerId,
            OrderNumber,
            Status,
            Subtotal,
            TaxTotal,
            GrandTotal,
            CurrencyCode,
            PlacedAt,
            CreatedAt,
            UpdatedAt,
            CancelledAt,
            CancellationReason
        )
        VALUES
        (
            @Id,
            @TenantId,
            @ShopId,
            @CustomerId,
            @OrderNumber,
            @Status,
            @Subtotal,
            @TaxTotal,
            @GrandTotal,
            @CurrencyCode,
            @PlacedAt,
            @CreatedAt,
            @UpdatedAt,
            NULL,
            NULL
        );
        """;

    private const string InsertOrderItemSql = """
        INSERT INTO dbo.OrderItems
        (
            Id,
            TenantId,
            OrderId,
            ProductId,
            Sku,
            ProductName,
            Quantity,
            UnitPrice,
            LineTotal
        )
        VALUES
        (
            @Id,
            @TenantId,
            @OrderId,
            @ProductId,
            @Sku,
            @ProductName,
            @Quantity,
            @UnitPrice,
            @LineTotal
        );
        """;

    public async Task InsertAsync(
        Order order,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        TenantGuard.Require(order.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertOrderSql,
                new
                {
                    order.Id,
                    order.TenantId,
                    order.ShopId,
                    order.CustomerId,
                    order.OrderNumber,
                    Status = (int)order.Status,
                    order.Subtotal,
                    order.TaxTotal,
                    order.GrandTotal,
                    order.CurrencyCode,
                    order.PlacedAt,
                    order.CreatedAt,
                    order.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Dapper turns an enumerable parameter into one round trip per element
        // against the same command; all of them share this transaction.
        var itemParameters = order.Items
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.OrderId,
                item.ProductId,
                item.Sku,
                item.ProductName,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal,
            })
            .ToList();

        if (itemParameters.Count > 0)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertOrderItemSql,
                    itemParameters,
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private const string UpdateSql = """
        UPDATE dbo.Orders
        SET
            Status             = @Status,
            UpdatedAt          = @UpdatedAt,
            CancelledAt        = @CancelledAt,
            CancellationReason = @CancellationReason
        WHERE TenantId = @TenantId
          AND Id       = @Id;
        """;

    public async Task UpdateAsync(
        Order order,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        TenantGuard.Require(order.TenantId);

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                UpdateSql,
                new
                {
                    order.Id,
                    order.TenantId,
                    Status = (int)order.Status,
                    order.UpdatedAt,
                    order.CancelledAt,
                    order.CancellationReason,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new DbConcurrencyException(
                $"Order '{order.Id}' was not updated. It does not exist in this tenant.");
        }
    }

    private const string InsertPaymentSql = """
        INSERT INTO dbo.Payments
        (
            Id,
            TenantId,
            OrderId,
            Provider,
            ProviderReference,
            Amount,
            CurrencyCode,
            Status,
            CreatedAt,
            UpdatedAt
        )
        VALUES
        (
            @Id,
            @TenantId,
            @OrderId,
            @Provider,
            @ProviderReference,
            @Amount,
            @CurrencyCode,
            @Status,
            @CreatedAt,
            @UpdatedAt
        );
        """;

    public async Task InsertPaymentAsync(
        Payment payment,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        TenantGuard.Require(payment.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertPaymentSql,
                new
                {
                    payment.Id,
                    payment.TenantId,
                    payment.OrderId,
                    payment.Provider,
                    payment.ProviderReference,
                    payment.Amount,
                    payment.CurrencyCode,
                    Status = (int)payment.Status,
                    payment.CreatedAt,
                    payment.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string InsertStatusHistorySql = """
        INSERT INTO dbo.OrderStatusHistory
        (
            Id,
            TenantId,
            OrderId,
            FromStatus,
            ToStatus,
            Reason,
            ChangedByUserId,
            OccurredAt
        )
        VALUES
        (
            @Id,
            @TenantId,
            @OrderId,
            @FromStatus,
            @ToStatus,
            @Reason,
            @ChangedByUserId,
            @OccurredAt
        );
        """;

    public async Task InsertStatusHistoryAsync(
        OrderStatusHistoryEntry entry,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        TenantGuard.Require(entry.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertStatusHistorySql,
                new
                {
                    entry.Id,
                    entry.TenantId,
                    entry.OrderId,
                    FromStatus = entry.FromStatus.HasValue ? (int)entry.FromStatus.Value : (int?)null,
                    ToStatus = (int)entry.ToStatus,
                    entry.Reason,
                    entry.ChangedByUserId,
                    entry.OccurredAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <remarks>
    /// <para>
    /// The sequence row is created on first use and then incremented in place.
    /// The UPDATE takes an exclusive lock on that single row for the rest of the
    /// transaction, which is what stops two concurrent checkouts in the same
    /// shop from being handed the same number.
    /// </para>
    /// <para>
    /// Numbers are consumed by the transaction, so a rollback leaves a gap. That
    /// is the intended trade: gaps are harmless, duplicates are not.
    /// </para>
    /// </remarks>
    private const string NextOrderNumberSql = """
        MERGE dbo.ShopOrderSequences WITH (HOLDLOCK) AS target
        USING (SELECT @TenantId AS TenantId, @ShopId AS ShopId) AS source
            ON  target.TenantId = source.TenantId
            AND target.ShopId   = source.ShopId
        WHEN MATCHED THEN
            UPDATE SET LastNumber = target.LastNumber + 1
        WHEN NOT MATCHED THEN
            INSERT (TenantId, ShopId, LastNumber)
            VALUES (source.TenantId, source.ShopId, 1)
        OUTPUT inserted.LastNumber;
        """;

    public async Task<string> NextOrderNumberAsync(
        Guid tenantId,
        Guid shopId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var sequence = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                NextOrderNumberSql,
                new { TenantId = tenantId, ShopId = shopId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return $"ORD-{sequence:D8}";
    }

    /// <summary>Row shape of the order header in <see cref="GetByIdSql"/>.</summary>
    private sealed class OrderRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid ShopId { get; init; }

        public Guid CustomerId { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public int Status { get; init; }

        public string CurrencyCode { get; init; } = string.Empty;

        public DateTime PlacedAt { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public DateTime? CancelledAt { get; init; }

        public string? CancellationReason { get; init; }

        public Order ToDomain(IEnumerable<OrderItem> items)
        {
            return Order.Restore(
                id: Id,
                tenantId: TenantId,
                shopId: ShopId,
                customerId: CustomerId,
                orderNumber: OrderNumber,
                status: (OrderStatus)Status,
                currencyCode: CurrencyCode,
                items: items,
                placedAt: PlacedAt,
                createdAt: CreatedAt,
                updatedAt: UpdatedAt,
                cancelledAt: CancelledAt,
                cancellationReason: CancellationReason);
        }
    }

    /// <summary>Row shape of the order items in <see cref="GetByIdSql"/>.</summary>
    private sealed class OrderItemRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid OrderId { get; init; }

        public Guid ProductId { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string ProductName { get; init; } = string.Empty;

        public int Quantity { get; init; }

        public decimal UnitPrice { get; init; }

        public OrderItem ToDomain()
        {
            return OrderItem.Restore(
                id: Id,
                tenantId: TenantId,
                orderId: OrderId,
                productId: ProductId,
                sku: Sku,
                productName: ProductName,
                quantity: Quantity,
                unitPrice: UnitPrice);
        }
    }
}
