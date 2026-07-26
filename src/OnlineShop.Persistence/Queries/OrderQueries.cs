using Dapper;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Queries;

/// <summary>
/// Order read model.
/// </summary>
/// <remarks>
/// <code>
/// OrderQueries -> ReadConnection -> Dapper -> read replica
/// </code>
/// </remarks>
internal sealed class OrderQueries : IOrderQueries
{
    private const int MaxPageSize = 200;

    private readonly IDbConnectionFactory _connectionFactory;

    public OrderQueries(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string GetListSql = """
        SELECT
            o.Id,
            o.ShopId,
            o.OrderNumber,
            o.CustomerId,
            c.Email AS CustomerEmail,
            o.Status,
            o.GrandTotal,
            o.CurrencyCode,
            (
                SELECT COUNT(1)
                FROM dbo.OrderItems AS oi
                WHERE oi.TenantId = o.TenantId
                  AND oi.OrderId  = o.Id
            ) AS ItemCount,
            o.PlacedAt
        FROM dbo.Orders AS o
        INNER JOIN dbo.Customers AS c
            ON  c.TenantId = o.TenantId
            AND c.Id       = o.CustomerId
        WHERE o.TenantId = @TenantId
          AND (@ShopId      IS NULL OR o.ShopId     = @ShopId)
          AND (@CustomerId  IS NULL OR o.CustomerId = @CustomerId)
          AND (@Status      IS NULL OR o.Status     = @Status)
          AND (@PlacedFrom  IS NULL OR o.PlacedAt  >= @PlacedFrom)
          AND (@PlacedTo    IS NULL OR o.PlacedAt  <  @PlacedTo)
          AND (@OrderNumber IS NULL OR o.OrderNumber LIKE @OrderNumberPattern ESCAPE '\')
        ORDER BY o.PlacedAt DESC, o.Id ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        OPTION (RECOMPILE);
        """;

    public async Task<IReadOnlyList<OrderListItemDto>> GetAsync(
        Guid tenantId,
        OrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);
        ArgumentNullException.ThrowIfNull(filter);

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<OrderListItemDto>(
            new CommandDefinition(
                GetListSql,
                BuildParameters(tenantId, filter, includePaging: true),
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }

    private const string CountSql = """
        SELECT COUNT_BIG(1)
        FROM dbo.Orders AS o
        WHERE o.TenantId = @TenantId
          AND (@ShopId      IS NULL OR o.ShopId     = @ShopId)
          AND (@CustomerId  IS NULL OR o.CustomerId = @CustomerId)
          AND (@Status      IS NULL OR o.Status     = @Status)
          AND (@PlacedFrom  IS NULL OR o.PlacedAt  >= @PlacedFrom)
          AND (@PlacedTo    IS NULL OR o.PlacedAt  <  @PlacedTo)
          AND (@OrderNumber IS NULL OR o.OrderNumber LIKE @OrderNumberPattern ESCAPE '\')
        OPTION (RECOMPILE);
        """;

    public async Task<int> CountAsync(
        Guid tenantId,
        OrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);
        ArgumentNullException.ThrowIfNull(filter);

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var total = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                CountSql,
                BuildParameters(tenantId, filter, includePaging: false),
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return (int)Math.Min(total, int.MaxValue);
    }

    /// <remarks>
    /// One round trip returning four result sets. Splitting this into four
    /// separate calls would mean four round trips and, on a replica, four
    /// chances to observe different points in the replication stream.
    /// </remarks>
    private const string GetDetailsSql = """
        SELECT
            o.Id,
            o.ShopId,
            s.Name   AS ShopName,
            o.OrderNumber,
            o.CustomerId,
            c.Email    AS CustomerEmail,
            c.FullName AS CustomerName,
            o.Status,
            o.Subtotal,
            o.TaxTotal,
            o.GrandTotal,
            o.CurrencyCode,
            o.PlacedAt,
            o.CancelledAt,
            o.CancellationReason
        FROM dbo.Orders AS o
        INNER JOIN dbo.Shops AS s
            ON  s.TenantId = o.TenantId
            AND s.Id       = o.ShopId
        INNER JOIN dbo.Customers AS c
            ON  c.TenantId = o.TenantId
            AND c.Id       = o.CustomerId
        WHERE o.TenantId = @TenantId
          AND o.Id       = @OrderId;

        SELECT
            oi.Id,
            oi.ProductId,
            oi.Sku,
            oi.ProductName,
            oi.Quantity,
            oi.UnitPrice,
            oi.LineTotal
        FROM dbo.OrderItems AS oi
        WHERE oi.TenantId = @TenantId
          AND oi.OrderId  = @OrderId
        ORDER BY oi.ProductName ASC, oi.Id ASC;

        SELECT
            p.Id,
            p.Provider,
            p.ProviderReference,
            p.Amount,
            p.CurrencyCode,
            p.Status,
            p.CreatedAt
        FROM dbo.Payments AS p
        WHERE p.TenantId = @TenantId
          AND p.OrderId  = @OrderId
        ORDER BY p.CreatedAt ASC, p.Id ASC;

        SELECT
            h.Id,
            h.FromStatus,
            h.ToStatus,
            h.Reason,
            h.OccurredAt
        FROM dbo.OrderStatusHistory AS h
        WHERE h.TenantId = @TenantId
          AND h.OrderId  = @OrderId
        ORDER BY h.OccurredAt ASC, h.Id ASC;
        """;

    public async Task<OrderDetailsDto?> GetDetailsAsync(
        Guid tenantId,
        Guid orderId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        // Eventual -> ReadConnection, Strong -> WriteConnection.
        using var connection = _connectionFactory.CreateConnection(consistency);
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(
                GetDetailsSql,
                new { TenantId = tenantId, OrderId = orderId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var header = await results.ReadSingleOrDefaultAsync<OrderHeaderRow>().ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var items = (await results.ReadAsync<OrderItemDto>().ConfigureAwait(false)).ToList();
        var payments = (await results.ReadAsync<PaymentDto>().ConfigureAwait(false)).ToList();
        var history = (await results.ReadAsync<OrderStatusHistoryDto>().ConfigureAwait(false)).ToList();

        return new OrderDetailsDto
        {
            Id = header.Id,
            ShopId = header.ShopId,
            ShopName = header.ShopName,
            OrderNumber = header.OrderNumber,
            CustomerId = header.CustomerId,
            CustomerEmail = header.CustomerEmail,
            CustomerName = header.CustomerName,
            Status = header.Status,
            Subtotal = header.Subtotal,
            TaxTotal = header.TaxTotal,
            GrandTotal = header.GrandTotal,
            CurrencyCode = header.CurrencyCode,
            PlacedAt = header.PlacedAt,
            CancelledAt = header.CancelledAt,
            CancellationReason = header.CancellationReason,
            Items = items,
            Payments = payments,
            StatusHistory = history,
        };
    }

    private static DynamicParameters BuildParameters(Guid tenantId, OrderFilter filter, bool includePaging)
    {
        var hasOrderNumber = !string.IsNullOrWhiteSpace(filter.OrderNumber);

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", tenantId);
        parameters.Add("ShopId", filter.ShopId);
        parameters.Add("CustomerId", filter.CustomerId);
        parameters.Add("Status", filter.Status.HasValue ? (int)filter.Status.Value : (int?)null);
        parameters.Add("PlacedFrom", filter.PlacedFromUtc);
        parameters.Add("PlacedTo", filter.PlacedToUtc);
        parameters.Add("OrderNumber", hasOrderNumber ? filter.OrderNumber!.Trim() : null);
        parameters.Add(
            "OrderNumberPattern",
            hasOrderNumber ? SqlLike.StartsWith(filter.OrderNumber!.Trim()) : null);

        if (includePaging)
        {
            var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
            var page = Math.Max(filter.Page, 1);

            parameters.Add("PageSize", pageSize);
            parameters.Add("Offset", (page - 1) * pageSize);
        }

        return parameters;
    }

    /// <summary>Shape of the first result set of <see cref="GetDetailsSql"/>.</summary>
    private sealed class OrderHeaderRow
    {
        public Guid Id { get; init; }

        public Guid ShopId { get; init; }

        public string ShopName { get; init; } = string.Empty;

        public string OrderNumber { get; init; } = string.Empty;

        public Guid CustomerId { get; init; }

        public string CustomerEmail { get; init; } = string.Empty;

        public string CustomerName { get; init; } = string.Empty;

        public Domain.Ordering.OrderStatus Status { get; init; }

        public decimal Subtotal { get; init; }

        public decimal TaxTotal { get; init; }

        public decimal GrandTotal { get; init; }

        public string CurrencyCode { get; init; } = string.Empty;

        public DateTime PlacedAt { get; init; }

        public DateTime? CancelledAt { get; init; }

        public string? CancellationReason { get; init; }
    }
}
