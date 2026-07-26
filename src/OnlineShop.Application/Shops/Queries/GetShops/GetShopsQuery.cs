using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Shops;

namespace OnlineShop.Application.Shops.Queries.GetShops;

/// <summary>
/// The shops belonging to the signed-in merchant's tenant.
/// </summary>
/// <remarks>
/// Carries a consistency option because the back-office redirects here after
/// creating a shop, and a merchant who cannot see the shop they just made would
/// reasonably assume it failed.
/// </remarks>
[StrongConsistencyAllowed(
    "The shop list is shown immediately after a shop is created. Replication lag would hide the new shop from " +
    "the merchant who just created it.")]
public sealed record GetShopsQuery(ReadConsistency Consistency = ReadConsistency.Eventual)
    : IQuery<IReadOnlyList<ShopListItemDto>>, ISupportsReadConsistency;

public sealed class GetShopsQueryHandler : IQueryHandler<GetShopsQuery, IReadOnlyList<ShopListItemDto>>
{
    private readonly IShopQueries _shopQueries;
    private readonly ITenantContext _tenantContext;

    public GetShopsQueryHandler(IShopQueries shopQueries, ITenantContext tenantContext)
    {
        _shopQueries = shopQueries;
        _tenantContext = tenantContext;
    }

    public Task<IReadOnlyList<ShopListItemDto>> Handle(GetShopsQuery request, CancellationToken cancellationToken) =>
        _shopQueries.ListAsync(_tenantContext.TenantId, request.Consistency, cancellationToken);
}
