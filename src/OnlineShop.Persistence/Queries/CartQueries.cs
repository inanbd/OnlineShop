using Dapper;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Carts;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Ordering;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Queries;

/// <summary>
/// Basket read model.
/// </summary>
/// <remarks>
/// <code>
/// CartQueries -> ReadConnection (or WriteConnection when the caller asks for
///                strong consistency) -> Dapper -> SQL Server
/// </code>
/// </remarks>
internal sealed class CartQueries : ICartQueries
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CartQueries(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string GetOpenCartSql = """
        SELECT TOP (1)
            c.Id,
            c.ShopId,
            s.Name         AS ShopName,
            s.Slug         AS ShopSlug,
            s.CurrencyCode
        FROM dbo.Carts AS c
        INNER JOIN dbo.Shops AS s
            ON  s.TenantId = c.TenantId
            AND s.Id       = c.ShopId
        WHERE c.TenantId   = @TenantId
          AND c.ShopId     = @ShopId
          AND c.CustomerId = @CustomerId
          AND c.Status     = @OpenStatus
        ORDER BY c.CreatedAt DESC;

        SELECT
            ci.ProductId,
            p.Name  AS ProductName,
            p.Sku,
            ci.Quantity,
            ci.UnitPrice,
            p.Price AS CurrentPrice,
            ISNULL(i.QuantityOnHand, 0) - ISNULL(i.QuantityReserved, 0) AS QuantityAvailable,
            CAST(CASE WHEN p.Status = @ActiveStatus AND p.DeletedAt IS NULL THEN 1 ELSE 0 END AS bit) AS IsActive
        FROM dbo.CartItems AS ci
        INNER JOIN dbo.Carts AS c
            ON  c.TenantId = ci.TenantId
            AND c.Id       = ci.CartId
        INNER JOIN dbo.Products AS p
            ON  p.TenantId = ci.TenantId
            AND p.Id       = ci.ProductId
        LEFT JOIN dbo.InventoryItems AS i
            ON  i.TenantId  = ci.TenantId
            AND i.ProductId = ci.ProductId
        WHERE ci.TenantId  = @TenantId
          AND c.ShopId     = @ShopId
          AND c.CustomerId = @CustomerId
          AND c.Status     = @OpenStatus
        ORDER BY p.Name ASC;
        """;

    public async Task<CartDto?> GetOpenCartAsync(
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        // Eventual -> ReadConnection, Strong -> WriteConnection.
        using var connection = _connectionFactory.CreateConnection(consistency);
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(
                GetOpenCartSql,
                new
                {
                    TenantId = tenantId,
                    ShopId = shopId,
                    CustomerId = customerId,
                    OpenStatus = (int)CartStatus.Open,
                    ActiveStatus = (int)ProductStatus.Active,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var header = await results.ReadSingleOrDefaultAsync<CartHeaderRow>().ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var lines = (await results.ReadAsync<CartLineDto>().ConfigureAwait(false)).ToList();

        return new CartDto
        {
            Id = header.Id,
            ShopId = header.ShopId,
            ShopName = header.ShopName,
            ShopSlug = header.ShopSlug,
            CurrencyCode = header.CurrencyCode,
            Lines = lines,
        };
    }

    private sealed class CartHeaderRow
    {
        public Guid Id { get; init; }

        public Guid ShopId { get; init; }

        public string ShopName { get; init; } = string.Empty;

        public string ShopSlug { get; init; } = string.Empty;

        public string CurrencyCode { get; init; } = string.Empty;
    }
}
