using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Customers;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Queries;

/// <summary>
/// Customer read model.
/// </summary>
/// <remarks>
/// <code>
/// CustomerQueries -> ReadConnection -> Dapper -> read replica
/// </code>
/// </remarks>
internal sealed class CustomerQueries : ICustomerQueries
{
    private const int MaxPageSize = 200;

    private readonly IDbConnectionFactory _connectionFactory;

    public CustomerQueries(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <remarks>
    /// Order totals are aggregated in a correlated subquery scoped to the same
    /// tenant. Cancelled orders are excluded from lifetime value.
    /// </remarks>
    private const string GetListSql = """
        SELECT
            c.Id,
            c.ShopId,
            c.Email,
            c.FullName,
            ISNULL(o.OrderCount, 0)    AS OrderCount,
            ISNULL(o.LifetimeValue, 0) AS LifetimeValue,
            o.LastOrderAt,
            c.CreatedAt
        FROM dbo.Customers AS c
        OUTER APPLY
        (
            SELECT
                COUNT(1)        AS OrderCount,
                SUM(x.GrandTotal) AS LifetimeValue,
                MAX(x.PlacedAt) AS LastOrderAt
            FROM dbo.Orders AS x
            WHERE x.TenantId   = c.TenantId
              AND x.CustomerId = c.Id
              AND x.Status <> @CancelledStatus
        ) AS o
        WHERE c.TenantId = @TenantId
          AND (@ShopId      IS NULL OR c.ShopId     = @ShopId)
          AND (@CreatedFrom IS NULL OR c.CreatedAt >= @CreatedFrom)
          AND (@CreatedTo   IS NULL OR c.CreatedAt <  @CreatedTo)
          AND (@SearchTerm  IS NULL
               OR c.Email    LIKE @SearchPattern ESCAPE '\'
               OR c.FullName LIKE @SearchPattern ESCAPE '\')
        ORDER BY c.CreatedAt DESC, c.Id ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        OPTION (RECOMPILE);
        """;

    public async Task<IReadOnlyList<CustomerListItemDto>> GetAsync(
        Guid tenantId,
        CustomerFilter filter,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);
        ArgumentNullException.ThrowIfNull(filter);

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var parameters = BuildFilterParameters(tenantId, filter);
        parameters.Add("CancelledStatus", (int)Domain.Ordering.OrderStatus.Cancelled);

        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var page = Math.Max(filter.Page, 1);
        parameters.Add("PageSize", pageSize);
        parameters.Add("Offset", (page - 1) * pageSize);

        var rows = await connection.QueryAsync<CustomerListItemDto>(
            new CommandDefinition(
                GetListSql,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }

    private const string CountSql = """
        SELECT COUNT_BIG(1)
        FROM dbo.Customers AS c
        WHERE c.TenantId = @TenantId
          AND (@ShopId      IS NULL OR c.ShopId     = @ShopId)
          AND (@CreatedFrom IS NULL OR c.CreatedAt >= @CreatedFrom)
          AND (@CreatedTo   IS NULL OR c.CreatedAt <  @CreatedTo)
          AND (@SearchTerm  IS NULL
               OR c.Email    LIKE @SearchPattern ESCAPE '\'
               OR c.FullName LIKE @SearchPattern ESCAPE '\')
        OPTION (RECOMPILE);
        """;

    public async Task<int> CountAsync(
        Guid tenantId,
        CustomerFilter filter,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);
        ArgumentNullException.ThrowIfNull(filter);

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var total = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                CountSql,
                BuildFilterParameters(tenantId, filter),
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return (int)Math.Min(total, int.MaxValue);
    }

    /// <summary>
    /// The predicates the list and the count have in common. Each caller adds
    /// only what its own statement declares, so no statement is handed a
    /// parameter it never uses.
    /// </summary>
    private static DynamicParameters BuildFilterParameters(Guid tenantId, CustomerFilter filter)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(filter.SearchTerm);

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", tenantId);
        parameters.Add("ShopId", filter.ShopId);
        parameters.Add("CreatedFrom", filter.CreatedFromUtc);
        parameters.Add("CreatedTo", filter.CreatedToUtc);
        parameters.Add("SearchTerm", hasSearch ? filter.SearchTerm!.Trim() : null);
        parameters.Add("SearchPattern", hasSearch ? SqlLike.Contains(filter.SearchTerm!.Trim()) : null);

        return parameters;
    }
}
