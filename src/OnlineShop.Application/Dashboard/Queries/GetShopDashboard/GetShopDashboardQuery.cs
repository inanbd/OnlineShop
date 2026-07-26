using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Dashboard;
using OnlineShop.Domain.Common;

namespace OnlineShop.Application.Dashboard.Queries.GetShopDashboard;

/// <summary>
/// Aggregated metrics for one shop over a trailing window.
/// </summary>
/// <remarks>
/// The heaviest read in the application: several aggregates over orders, order
/// items, customers and inventory. Keeping it on <c>ReadConnection</c> is the
/// main reason the read replica exists — analytics scans must not compete with
/// checkout for resources on the primary. There is deliberately no strong
/// consistency option.
/// </remarks>
public sealed record GetShopDashboardQuery(Guid ShopId, int WindowDays = 30)
    : IQuery<ShopDashboardDto>;

public sealed class GetShopDashboardQueryHandler
    : IQueryHandler<GetShopDashboardQuery, ShopDashboardDto>
{
    private const int MinWindowDays = 1;
    private const int MaxWindowDays = 365;

    private readonly IDashboardQueries _dashboardQueries;
    private readonly ITenantContext _tenantContext;

    public GetShopDashboardQueryHandler(IDashboardQueries dashboardQueries, ITenantContext tenantContext)
    {
        _dashboardQueries = dashboardQueries;
        _tenantContext = tenantContext;
    }

    public async Task<ShopDashboardDto> Handle(
        GetShopDashboardQuery request,
        CancellationToken cancellationToken)
    {
        if (request.WindowDays is < MinWindowDays or > MaxWindowDays)
        {
            throw new DomainException(
                $"Dashboard window must be between {MinWindowDays} and {MaxWindowDays} days.");
        }

        var dashboard = await _dashboardQueries
            .GetShopDashboardAsync(_tenantContext.TenantId, request.ShopId, request.WindowDays, cancellationToken)
            .ConfigureAwait(false);

        return dashboard ?? throw new NotFoundException(nameof(Domain.Shops.Shop), request.ShopId);
    }
}
