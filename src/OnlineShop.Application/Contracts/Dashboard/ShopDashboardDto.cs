using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Contracts.Dashboard;

/// <summary>
/// Aggregated shop metrics. Reporting data tolerates replication lag, so this
/// is always served from <c>ReadConnection</c>.
/// </summary>
public sealed class ShopDashboardDto
{
    public Guid ShopId { get; init; }

    public string ShopName { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public DateTime GeneratedAtUtc { get; init; }

    public int WindowDays { get; init; }

    public decimal RevenueInWindow { get; init; }

    public int OrdersInWindow { get; init; }

    public decimal AverageOrderValue { get; init; }

    public int NewCustomersInWindow { get; init; }

    public int ActiveProductCount { get; init; }

    public int LowStockProductCount { get; init; }

    public IReadOnlyList<OrderStatusCountDto> OrdersByStatus { get; init; } = [];

    public IReadOnlyList<TopProductDto> TopProducts { get; init; } = [];

    public IReadOnlyList<DailyRevenueDto> DailyRevenue { get; init; } = [];
}

public sealed class OrderStatusCountDto
{
    public OrderStatus Status { get; init; }

    public int Count { get; init; }
}

public sealed class TopProductDto
{
    public Guid ProductId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public int UnitsSold { get; init; }

    public decimal Revenue { get; init; }
}

public sealed class DailyRevenueDto
{
    public DateTime Day { get; init; }

    public decimal Revenue { get; init; }

    public int OrderCount { get; init; }
}
