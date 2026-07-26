using Dapper;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Queries;

/// <summary>
/// Catalog read model.
/// </summary>
/// <remarks>
/// <code>
/// ProductQueries -> ReadConnection -> Dapper -> read replica
/// </code>
/// Every statement here is a SELECT filtered by <c>TenantId</c>, and every
/// value reaching SQL is a parameter.
/// </remarks>
internal sealed class ProductQueries : IProductQueries
{
    private const int MaxPageSize = 200;

    private readonly IDbConnectionFactory _connectionFactory;

    public ProductQueries(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string GetByIdSql = """
        SELECT
            p.Id,
            p.ShopId,
            s.Name              AS ShopName,
            p.Name,
            p.Sku,
            p.Description,
            p.Price,
            p.CurrencyCode,
            p.Status,
            ISNULL(i.QuantityOnHand, 0)                                 AS QuantityOnHand,
            ISNULL(i.QuantityReserved, 0)                               AS QuantityReserved,
            ISNULL(i.QuantityOnHand, 0) - ISNULL(i.QuantityReserved, 0) AS QuantityAvailable,
            p.CreatedAt,
            p.UpdatedAt
        FROM dbo.Products AS p
        INNER JOIN dbo.Shops AS s
            ON  s.TenantId = p.TenantId
            AND s.Id       = p.ShopId
        LEFT JOIN dbo.InventoryItems AS i
            ON  i.TenantId  = p.TenantId
            AND i.ProductId = p.Id
        WHERE p.TenantId = @TenantId
          AND p.Id       = @ProductId
          AND p.DeletedAt IS NULL;
        """;

    public async Task<ProductDto?> GetByIdAsync(
        Guid tenantId,
        Guid productId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        // Eventual -> ReadConnection, Strong -> WriteConnection.
        using var connection = _connectionFactory.CreateConnection(consistency);
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<ProductDto>(
            new CommandDefinition(
                GetByIdSql,
                new { TenantId = tenantId, ProductId = productId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <remarks>
    /// The optional filters use the <c>(@Param IS NULL OR column = @Param)</c>
    /// form so the statement text is constant and every value stays a
    /// parameter. That form can otherwise cache a plan built for one filter
    /// combination and reuse it for a very different one, so the statement ends
    /// with OPTION (RECOMPILE).
    /// </remarks>
    private const string GetListSql = """
        SELECT
            p.Id,
            p.ShopId,
            p.Name,
            p.Sku,
            p.Price,
            p.CurrencyCode,
            p.Status,
            ISNULL(i.QuantityOnHand, 0) - ISNULL(i.QuantityReserved, 0) AS QuantityAvailable,
            p.UpdatedAt
        FROM dbo.Products AS p
        LEFT JOIN dbo.InventoryItems AS i
            ON  i.TenantId  = p.TenantId
            AND i.ProductId = p.Id
        WHERE p.TenantId = @TenantId
          AND (@ShopId     IS NULL OR p.ShopId = @ShopId)
          AND (@Status     IS NULL OR p.Status = @Status)
          AND (@MinPrice   IS NULL OR p.Price >= @MinPrice)
          AND (@MaxPrice   IS NULL OR p.Price <= @MaxPrice)
          AND (@IncludeDeleted = 1 OR p.DeletedAt IS NULL)
          AND (@SearchTerm IS NULL
               OR p.Name LIKE @SearchPattern ESCAPE '\'
               OR p.Sku  LIKE @SearchPattern ESCAPE '\')
        ORDER BY p.Name ASC, p.Id ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        OPTION (RECOMPILE);
        """;

    public async Task<IReadOnlyList<ProductListItemDto>> GetAsync(
        Guid tenantId,
        ProductFilter filter,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);
        ArgumentNullException.ThrowIfNull(filter);

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ProductListItemDto>(
            new CommandDefinition(
                GetListSql,
                BuildParameters(tenantId, filter, includePaging: true),
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToList();
    }

    private const string CountSql = """
        SELECT COUNT_BIG(1)
        FROM dbo.Products AS p
        WHERE p.TenantId = @TenantId
          AND (@ShopId     IS NULL OR p.ShopId = @ShopId)
          AND (@Status     IS NULL OR p.Status = @Status)
          AND (@MinPrice   IS NULL OR p.Price >= @MinPrice)
          AND (@MaxPrice   IS NULL OR p.Price <= @MaxPrice)
          AND (@IncludeDeleted = 1 OR p.DeletedAt IS NULL)
          AND (@SearchTerm IS NULL
               OR p.Name LIKE @SearchPattern ESCAPE '\'
               OR p.Sku  LIKE @SearchPattern ESCAPE '\')
        OPTION (RECOMPILE);
        """;

    public async Task<int> CountAsync(
        Guid tenantId,
        ProductFilter filter,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);
        ArgumentNullException.ThrowIfNull(filter);

        using var connection = _connectionFactory.CreateReadConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var total = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                CountSql,
                BuildParameters(tenantId, filter, includePaging: false),
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return (int)Math.Min(total, int.MaxValue);
    }

    private static DynamicParameters BuildParameters(Guid tenantId, ProductFilter filter, bool includePaging)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(filter.SearchTerm);

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", tenantId);
        parameters.Add("ShopId", filter.ShopId);
        parameters.Add("Status", filter.Status.HasValue ? (int)filter.Status.Value : (int?)null);
        parameters.Add("MinPrice", filter.MinPrice);
        parameters.Add("MaxPrice", filter.MaxPrice);
        parameters.Add("IncludeDeleted", filter.IncludeDeleted ? 1 : 0);
        parameters.Add("SearchTerm", hasSearch ? filter.SearchTerm!.Trim() : null);
        parameters.Add("SearchPattern", hasSearch ? SqlLike.Contains(filter.SearchTerm!.Trim()) : null);

        if (includePaging)
        {
            var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
            var page = Math.Max(filter.Page, 1);

            parameters.Add("PageSize", pageSize);
            parameters.Add("Offset", (page - 1) * pageSize);
        }

        return parameters;
    }
}
