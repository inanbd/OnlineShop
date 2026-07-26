using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Shops;

namespace OnlineShop.Application.Shops.Queries.GetShopMembers;

/// <summary>People with access to a shop.</summary>
[StrongConsistencyAllowed(
    "The member list is shown immediately after an invitation is sent, and an invitation that appears not to " +
    "have been created invites a duplicate.")]
public sealed record GetShopMembersQuery(
    Guid ShopId,
    ReadConsistency Consistency = ReadConsistency.Eventual)
    : IQuery<IReadOnlyList<ShopMemberDto>>, ISupportsReadConsistency;

public sealed class GetShopMembersQueryHandler
    : IQueryHandler<GetShopMembersQuery, IReadOnlyList<ShopMemberDto>>
{
    private readonly IShopQueries _shopQueries;
    private readonly ITenantContext _tenantContext;

    public GetShopMembersQueryHandler(IShopQueries shopQueries, ITenantContext tenantContext)
    {
        _shopQueries = shopQueries;
        _tenantContext = tenantContext;
    }

    public Task<IReadOnlyList<ShopMemberDto>> Handle(
        GetShopMembersQuery request,
        CancellationToken cancellationToken) =>
        _shopQueries.GetMembersAsync(
            _tenantContext.TenantId, request.ShopId, request.Consistency, cancellationToken);
}
