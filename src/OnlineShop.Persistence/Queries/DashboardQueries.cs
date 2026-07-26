using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Dashboard;
using OnlineShop.Domain.Ordering;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Queries;

/// <summary>
/// Shop analytics read model.
/// </summary>
/// <remarks>
/// <code>
/// DashboardQueries -> ReadConnection -> Dapper -> read replica
/// </code>
/// <para>
/// The heaviest read path in the application. It offers no strong-consistency
/// option at all: an aggregate over the last 30 days does not change
/// meaningfully with a few seconds of replication lag, and running these scans
/// on the primary would put reporting in contention with checkout.
/// </para>
/// </remarks>
internal sealed class DashboardQueries : IDashboardQueries
{
    private const int TopProductCount = 5;

    private readonly IDbConnectionFactory _connectionFactory;

    public DashboardQueries(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <remarks>
    /// Six result sets in one round trip, all scoped to the same tenant, shop
    /// and time window, so every panel on the dashboard reflects one consistent
    /// snapshot.
    /// </remarks>
    private const string DashboardSql = """
        -- 1. Shop header. Also proves the shop belongs to this tenant.
        SELECT
            s.Id   AS ShopId,
            s.Name AS ShopName,
            s.CurrencyCode
        FROM dbo.Shops AS s
        WHERE s.TenantId = @TenantId
          AND s.Id       = @ShopId;

        -- 2. Revenue and order totals over the window, excluding cancellations.
        SELECT
            ISNULL(SUM(o.GrandTotal), 0) AS RevenueInWindow,
            COUNT(1)                     AS OrdersInWindow
        FROM dbo.Orders AS o
        WHERE o.TenantId = @TenantId
          AND o.ShopId   = @ShopId
          AND o.PlacedAt >= @WindowStart
          AND o.Status  <> @CancelledStatus;

        -- 3. Order counts by status over the window.
        SELECT
            o.Status,
            COUNT(1) AS Count
        FROM dbo.Orders AS o
        WHERE o.TenantId = @TenantId
          AND o.ShopId   = @ShopId
          AND o.PlacedAt >= @WindowStart
        GROUP BY o.Status;

        -- 4. Best sellers over the window by revenue.
        SELECT TOP (@TopProductCount)
            oi.ProductId,
            MAX(oi.ProductName)   AS Name,
            MAX(oi.Sku)           AS Sku,
            SUM(oi.Quantity)      AS UnitsSold,
            SUM(oi.LineTotal)     AS Revenue
        FROM dbo.OrderItems AS oi
        INNER JOIN dbo.Orders AS o
            ON  o.TenantId = oi.TenantId
            AND o.Id       = oi.OrderId
        WHERE oi.TenantId = @TenantId
          AND o.ShopId    = @ShopId
          AND o.PlacedAt >= @WindowStart
          AND o.Status   <> @CancelledStatus
        GROUP BY oi.ProductId
        ORDER BY Revenue DESC;

        -- 5. Daily revenue for the trend line.
        SELECT
            CAST(o.PlacedAt AS date)     AS Day,
            ISNULL(SUM(o.GrandTotal), 0) AS Revenue,
            COUNT(1)                     AS OrderCount
        FROM dbo.Orders AS o
        WHERE o.TenantId = @TenantId
          AND o.ShopId   = @ShopId
          AND o.PlacedAt >= @WindowStart
          AND o.Status  <> @CancelledStatus
        GROUP BY CAST(o.PlacedAt AS date)
        ORDER BY Day ASC;

        -- 6. Catalog and customer counters.
        SELECT
            (
                SELECT COUNT(1)
                FROM dbo.Customers AS c
                WHERE c.TenantId  = @TenantId
                  AND c.ShopId    = @ShopId
                  AND c.CreatedAt >= @WindowStart
            ) AS NewCustomersInWindow,
            (
                SELECT COUNT(1)
                FROM dbo.Products AS p
                WHERE p.TenantId = @TenantId
                  AND p.ShopId   = @ShopId
                  AND p.Status   = @ActiveProductStatus
                  AND p.DeletedAt IS NULL
            ) AS ActiveProductCount,
            (
                SELECT COUNT(1)
                FROM dbo.InventoryItems AS i
                INNER JOIN dbo.Products AS p
                    ON  p.TenantId = i.TenantId
                    AND p.Id       = i.ProductId
                WHERE i.TenantId = @TenantId
                  AND i.ShopId   = @ShopId
                  AND p.DeletedAt IS NULL
                  AND (i.QuantityOnHand - i.QuantityReserved) <= i.ReorderThreshold
            ) AS LowStockProductCount;
        """;

    public async Task<ShopDashboardDto?> GetShopDashboardAsync(
        Guid tenantId,
        Guid shopId,
        int windowDays,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var generatedAt = DateTime.UtcNow;
        var windowStart = generatedAt.AddDays(-windowDays);

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(
                DashboardSql,
                new
                {
                    TenantId = tenantId,
                    ShopId = shopId,
                    WindowStart = windowStart,
                    CancelledStatus = (int)OrderStatus.Cancelled,
                    ActiveProductStatus = (int)Domain.Catalog.ProductStatus.Active,
                    TopProductCount,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var header = await results.ReadSingleOrDefaultAsync<ShopHeaderRow>().ConfigureAwait(false);

        if (header is null)
        {
            // No shop with that id in this tenant.
            return null;
        }

        var totals = await results.ReadSingleAsync<RevenueTotalsRow>().ConfigureAwait(false);
        var byStatus = (await results.ReadAsync<OrderStatusCountDto>().ConfigureAwait(false)).ToList();
        var topProducts = (await results.ReadAsync<TopProductDto>().ConfigureAwait(false)).ToList();
        var dailyRevenue = (await results.ReadAsync<DailyRevenueDto>().ConfigureAwait(false)).ToList();
        var counters = await results.ReadSingleAsync<CountersRow>().ConfigureAwait(false);

        return new ShopDashboardDto
        {
            ShopId = header.ShopId,
            ShopName = header.ShopName,
            CurrencyCode = header.CurrencyCode,
            GeneratedAtUtc = generatedAt,
            WindowDays = windowDays,
            RevenueInWindow = totals.RevenueInWindow,
            OrdersInWindow = totals.OrdersInWindow,
            AverageOrderValue = totals.OrdersInWindow == 0
                ? 0m
                : decimal.Round(totals.RevenueInWindow / totals.OrdersInWindow, 2, MidpointRounding.AwayFromZero),
            NewCustomersInWindow = counters.NewCustomersInWindow,
            ActiveProductCount = counters.ActiveProductCount,
            LowStockProductCount = counters.LowStockProductCount,
            OrdersByStatus = byStatus,
            TopProducts = topProducts,
            DailyRevenue = dailyRevenue,
        };
    }

    private sealed class ShopHeaderRow
    {
        public Guid ShopId { get; init; }

        public string ShopName { get; init; } = string.Empty;

        public string CurrencyCode { get; init; } = string.Empty;
    }

    private sealed class RevenueTotalsRow
    {
        public decimal RevenueInWindow { get; init; }

        public int OrdersInWindow { get; init; }
    }

    private sealed class CountersRow
    {
        public int NewCustomersInWindow { get; init; }

        public int ActiveProductCount { get; init; }

        public int LowStockProductCount { get; init; }
    }
}
