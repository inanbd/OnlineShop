using OnlineShop.Application.Contracts.Dashboard;

namespace OnlineShop.Application.Abstractions.Persistence.Queries;

/// <summary>
/// Read model for shop analytics. Implemented by <c>DashboardQueries</c> over
/// <c>ReadConnection</c>.
/// </summary>
/// <remarks>
/// Dashboard aggregation is the workload that most benefits from a read
/// replica: it is heavy, it is tolerant of a few seconds of lag, and keeping it
/// off the primary protects checkout latency. There is deliberately no
/// consistency parameter here — analytics never reads the primary.
/// </remarks>
public interface IDashboardQueries
{
    Task<ShopDashboardDto?> GetShopDashboardAsync(
        Guid tenantId,
        Guid shopId,
        int windowDays,
        CancellationToken cancellationToken = default);
}
