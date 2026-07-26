using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Orders;

namespace OnlineShop.Application.Orders.Queries.GetOrderDetails;

/// <summary>
/// Full order projection: header, line items, payments and status history.
/// </summary>
/// <remarks>
/// Checkout redirects the shopper straight to this view. That read happens
/// within milliseconds of the write transaction committing, so it passes
/// <see cref="ReadConsistency.Strong"/> and is served from the primary; an
/// order confirmation page that cannot find the order is not an acceptable
/// outcome. Every other caller — order history, support lookups — leaves the
/// default and reads the replica.
/// </remarks>
[StrongConsistencyAllowed(
    "Checkout redirects to the order confirmation page immediately after the write transaction commits. " +
    "Replication lag would show the shopper a missing order for the purchase they just completed.")]
public sealed record GetOrderDetailsQuery(
    Guid OrderId,
    ReadConsistency Consistency = ReadConsistency.Eventual)
    : IQuery<OrderDetailsDto>, ISupportsReadConsistency;

public sealed class GetOrderDetailsQueryHandler
    : IQueryHandler<GetOrderDetailsQuery, OrderDetailsDto>
{
    private readonly IOrderQueries _orderQueries;
    private readonly ITenantContext _tenantContext;

    public GetOrderDetailsQueryHandler(IOrderQueries orderQueries, ITenantContext tenantContext)
    {
        _orderQueries = orderQueries;
        _tenantContext = tenantContext;
    }

    public async Task<OrderDetailsDto> Handle(
        GetOrderDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var order = await _orderQueries
            .GetDetailsAsync(_tenantContext.TenantId, request.OrderId, request.Consistency, cancellationToken)
            .ConfigureAwait(false);

        return order ?? throw new NotFoundException(nameof(Domain.Ordering.Order), request.OrderId);
    }
}
