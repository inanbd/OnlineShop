using OnlineShop.Application.Contracts.Shops;

namespace OnlineShop.Application.Abstractions.Persistence.Queries;

/// <summary>
/// Read model for a tenant's own shops and their membership.
/// </summary>
/// <remarks>
/// Distinct from <see cref="IShopDirectory"/>, which resolves public storefront
/// slugs across all tenants. Everything here is tenant-filtered as usual.
/// </remarks>
public interface IShopQueries
{
    Task<IReadOnlyList<ShopListItemDto>> ListAsync(
        Guid tenantId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopMemberDto>> GetMembersAsync(
        Guid tenantId,
        Guid shopId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default);
}
