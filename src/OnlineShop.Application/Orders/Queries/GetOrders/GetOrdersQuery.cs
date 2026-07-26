using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts;
using OnlineShop.Application.Contracts.Orders;

namespace OnlineShop.Application.Orders.Queries.GetOrders;

/// <summary>
/// Order listing for the merchant back office and the customer's order history.
/// </summary>
/// <remarks>
/// Read-only reporting over a potentially large table: exactly what belongs on
/// the replica. No consistency knob, so it always uses <c>ReadConnection</c>.
/// </remarks>
public sealed record GetOrdersQuery(OrderFilter Filter)
    : IQuery<PagedResult<OrderListItemDto>>;

public sealed class GetOrdersQueryHandler
    : IQueryHandler<GetOrdersQuery, PagedResult<OrderListItemDto>>
{
    private readonly IOrderQueries _orderQueries;
    private readonly ITenantContext _tenantContext;

    public GetOrdersQueryHandler(IOrderQueries orderQueries, ITenantContext tenantContext)
    {
        _orderQueries = orderQueries;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<OrderListItemDto>> Handle(
        GetOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var filter = request.Filter;

        var totalCount = await _orderQueries
            .CountAsync(tenantId, filter, cancellationToken)
            .ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<OrderListItemDto>.Empty(filter.Page, filter.PageSize);
        }

        var items = await _orderQueries
            .GetAsync(tenantId, filter, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<OrderListItemDto>(items, totalCount, filter.Page, filter.PageSize);
    }
}
