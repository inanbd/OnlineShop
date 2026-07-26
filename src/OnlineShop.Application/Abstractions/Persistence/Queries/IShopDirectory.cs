namespace OnlineShop.Application.Abstractions.Persistence.Queries;

/// <summary>Enough about a shop to route a public storefront URL to it.</summary>
public sealed record ShopDirectoryEntry(
    Guid TenantId,
    Guid ShopId,
    string Name,
    string Slug,
    string CurrencyCode,
    int ActiveProductCount);

/// <summary>
/// Resolves public storefront addresses to a tenant and shop.
/// </summary>
/// <remarks>
/// <para>
/// The one read in the application that is deliberately <b>not</b> tenant
/// scoped, and it has to be: a shopper arriving at <c>/shop/acme</c> has no
/// tenant yet. Resolving that slug is what establishes which tenant the rest of
/// the request runs as.
/// </para>
/// <para>
/// Only public storefront identity is exposed — name, slug, currency, a product
/// count. Nothing here reveals another tenant's catalog, orders or customers;
/// every one of those reads is tenant-filtered as usual, using the tenant this
/// lookup resolved.
/// </para>
/// <para>Served from <c>ReadConnection</c> like any other query.</para>
/// </remarks>
public interface IShopDirectory
{
    Task<ShopDirectoryEntry?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopDirectoryEntry>> ListActiveAsync(CancellationToken cancellationToken = default);
}
