using Dapper;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Shops;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Queries;

/// <summary>
/// Read model for a tenant's shops and membership.
/// </summary>
/// <remarks>
/// <code>
/// ShopQueries -> ReadConnection -> Dapper -> read replica
/// </code>
/// <para>
/// Both reads accept a consistency level, because the back-office lands on them
/// straight after creating a shop or inviting a member. Everywhere else they
/// use the replica.
/// </para>
/// </remarks>
internal sealed class ShopQueries : IShopQueries
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ShopQueries(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string ListSql = """
        SELECT
            s.Id,
            s.Name,
            s.Slug,
            s.CurrencyCode,
            s.IsActive,
            (
                SELECT COUNT(1)
                FROM dbo.Products AS p
                WHERE p.TenantId  = s.TenantId
                  AND p.ShopId    = s.Id
                  AND p.DeletedAt IS NULL
            ) AS ProductCount,
            (
                SELECT COUNT(1)
                FROM dbo.Orders AS o
                WHERE o.TenantId = s.TenantId
                  AND o.ShopId   = s.Id
            ) AS OrderCount,
            s.CreatedAt
        FROM dbo.Shops AS s
        WHERE s.TenantId = @TenantId
        ORDER BY s.Name ASC, s.Id ASC;
        """;

    public async Task<IReadOnlyList<ShopListItemDto>> ListAsync(
        Guid tenantId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        using var connection = _connectionFactory.CreateConnection(consistency);
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ShopListItemDto>(
            new CommandDefinition(
                ListSql,
                new { TenantId = tenantId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }

    private const string GetMembersSql = """
        SELECT
            m.Id,
            m.Email,
            m.Role,
            m.Status,
            m.InvitedAt,
            m.AcceptedAt
        FROM dbo.ShopMembers AS m
        WHERE m.TenantId = @TenantId
          AND m.ShopId   = @ShopId
        ORDER BY m.Role DESC, m.Email ASC;
        """;

    public async Task<IReadOnlyList<ShopMemberDto>> GetMembersAsync(
        Guid tenantId,
        Guid shopId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        using var connection = _connectionFactory.CreateConnection(consistency);
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ShopMemberDto>(
            new CommandDefinition(
                GetMembersSql,
                new { TenantId = tenantId, ShopId = shopId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }
}
