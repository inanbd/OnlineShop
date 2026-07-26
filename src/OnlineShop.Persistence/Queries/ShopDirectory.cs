using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Domain.Catalog;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Queries;

/// <summary>
/// Public storefront directory.
/// </summary>
/// <remarks>
/// <code>
/// ShopDirectory -> ReadConnection -> Dapper -> read replica
/// </code>
/// <para>
/// The statements here are intentionally not filtered by <c>TenantId</c>: this
/// is the lookup that <em>establishes</em> the tenant for a storefront request,
/// so there is nothing to filter by yet. It is listed by name in the
/// tenant-isolation test's documented exceptions, so the omission is recorded
/// rather than accidental.
/// </para>
/// <para>
/// It returns only what a public shop front already discloses. Every read that
/// follows — catalog, cart, orders — is scoped to the tenant resolved here.
/// </para>
/// </remarks>
internal sealed class ShopDirectory : IShopDirectory
{
    private const int MaxShopsListed = 100;

    private readonly IDbConnectionFactory _connectionFactory;

    public ShopDirectory(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string FindBySlugSql = """
        SELECT
            s.TenantId,
            s.Id AS ShopId,
            s.Name,
            s.Slug,
            s.CurrencyCode,
            (
                SELECT COUNT(1)
                FROM dbo.Products AS p
                WHERE p.TenantId  = s.TenantId
                  AND p.ShopId    = s.Id
                  AND p.Status    = @ActiveStatus
                  AND p.DeletedAt IS NULL
            ) AS ActiveProductCount
        FROM dbo.Shops AS s
        WHERE s.Slug     = @Slug
          AND s.IsActive = 1;
        """;

    public async Task<ShopDirectoryEntry?> FindBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<ShopDirectoryEntry>(
            new CommandDefinition(
                FindBySlugSql,
                new
                {
                    Slug = slug.Trim().ToLowerInvariant(),
                    ActiveStatus = (int)ProductStatus.Active,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string ListActiveSql = """
        SELECT TOP (@MaxShops)
            s.TenantId,
            s.Id AS ShopId,
            s.Name,
            s.Slug,
            s.CurrencyCode,
            (
                SELECT COUNT(1)
                FROM dbo.Products AS p
                WHERE p.TenantId  = s.TenantId
                  AND p.ShopId    = s.Id
                  AND p.Status    = @ActiveStatus
                  AND p.DeletedAt IS NULL
            ) AS ActiveProductCount
        FROM dbo.Shops AS s
        WHERE s.IsActive = 1
        ORDER BY s.Name ASC, s.Id ASC;
        """;

    public async Task<IReadOnlyList<ShopDirectoryEntry>> ListActiveAsync(
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ShopDirectoryEntry>(
            new CommandDefinition(
                ListActiveSql,
                new { MaxShops = MaxShopsListed, ActiveStatus = (int)ProductStatus.Active },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }
}
