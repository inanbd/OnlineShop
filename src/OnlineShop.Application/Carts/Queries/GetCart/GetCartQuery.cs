using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Carts;

namespace OnlineShop.Application.Carts.Queries.GetCart;

/// <summary>
/// The shopper's open basket for a shop.
/// </summary>
/// <remarks>
/// The most frequently hit read-after-write path in a storefront. Adding an
/// item and being redirected to the basket happens within milliseconds, and a
/// replica that has not caught up would show the shopper an empty basket
/// immediately after they filled it. Those redirects pass
/// <see cref="ReadConsistency.Strong"/>; simply opening the basket from a
/// navigation link does not.
/// </remarks>
[StrongConsistencyAllowed(
    "The basket page is shown immediately after adding, changing or removing a line. Serving that read from a " +
    "replica would show the shopper a basket that does not contain what they just put in it.")]
public sealed record GetCartQuery(
    Guid ShopId,
    Guid CustomerId,
    ReadConsistency Consistency = ReadConsistency.Eventual)
    : IQuery<CartDto?>, ISupportsReadConsistency;

public sealed class GetCartQueryHandler : IQueryHandler<GetCartQuery, CartDto?>
{
    private readonly ICartQueries _cartQueries;
    private readonly ITenantContext _tenantContext;

    public GetCartQueryHandler(ICartQueries cartQueries, ITenantContext tenantContext)
    {
        _cartQueries = cartQueries;
        _tenantContext = tenantContext;
    }

    /// <returns>Null when the shopper has no open basket yet, which is not an error.</returns>
    public Task<CartDto?> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        return _cartQueries.GetOpenCartAsync(
            _tenantContext.TenantId,
            request.ShopId,
            request.CustomerId,
            request.Consistency,
            cancellationToken);
    }
}
