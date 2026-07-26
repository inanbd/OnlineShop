using System.Data;
using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Catalog;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Repositories;

/// <summary>
/// Catalog write repository.
/// </summary>
/// <remarks>
/// <code>
/// ProductRepository -> the caller's transaction -> WriteConnection -> primary
/// </code>
/// <para>
/// Note what is missing: there is no <c>IDbConnectionFactory</c> field. The
/// connection comes from the <see cref="IDbTransaction"/> the caller passes in,
/// so this class structurally cannot open a second connection in the middle of
/// someone else's transaction.
/// </para>
/// </remarks>
internal sealed class ProductRepository : IProductRepository
{
    private const string GetByIdSql = """
        SELECT
            p.Id,
            p.TenantId,
            p.ShopId,
            p.Name,
            p.Sku,
            p.Description,
            p.Price,
            p.CurrencyCode,
            p.Status,
            p.CreatedAt,
            p.UpdatedAt,
            p.DeletedAt
        FROM dbo.Products AS p
        WHERE p.TenantId = @TenantId
          AND p.Id       = @ProductId;
        """;

    public async Task<Product?> GetByIdAsync(
        Guid tenantId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<ProductRow>(
            new CommandDefinition(
                GetByIdSql,
                new { TenantId = tenantId, ProductId = productId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    private const string SkuExistsSql = """
        SELECT CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Products AS p
            WHERE p.TenantId = @TenantId
              AND p.ShopId   = @ShopId
              AND p.Sku      = @Sku
              AND p.DeletedAt IS NULL
              AND (@ExcludingProductId IS NULL OR p.Id <> @ExcludingProductId)
        ) THEN 1 ELSE 0 END;
        """;

    public async Task<bool> SkuExistsAsync(
        Guid tenantId,
        Guid shopId,
        string sku,
        Guid? excludingProductId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                SkuExistsSql,
                new
                {
                    TenantId = tenantId,
                    ShopId = shopId,
                    Sku = sku.Trim().ToUpperInvariant(),
                    ExcludingProductId = excludingProductId,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string InsertSql = """
        INSERT INTO dbo.Products
        (
            Id,
            TenantId,
            ShopId,
            Name,
            Sku,
            Description,
            Price,
            CurrencyCode,
            Status,
            CreatedAt,
            UpdatedAt,
            DeletedAt
        )
        VALUES
        (
            @Id,
            @TenantId,
            @ShopId,
            @Name,
            @Sku,
            @Description,
            @Price,
            @CurrencyCode,
            @Status,
            @CreatedAt,
            @UpdatedAt,
            NULL
        );
        """;

    public async Task InsertAsync(
        Product product,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        TenantGuard.Require(product.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                new
                {
                    product.Id,
                    product.TenantId,
                    product.ShopId,
                    product.Name,
                    product.Sku,
                    product.Description,
                    product.Price,
                    product.CurrencyCode,
                    Status = (int)product.Status,
                    product.CreatedAt,
                    product.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string UpdateSql = """
        UPDATE dbo.Products
        SET
            Name        = @Name,
            Sku         = @Sku,
            Description = @Description,
            Price       = @Price,
            Status      = @Status,
            UpdatedAt   = @UpdatedAt
        WHERE TenantId = @TenantId
          AND Id       = @Id
          AND DeletedAt IS NULL;
        """;

    public async Task UpdateAsync(
        Product product,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        TenantGuard.Require(product.TenantId);

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                UpdateSql,
                new
                {
                    product.Id,
                    product.TenantId,
                    product.Name,
                    product.Sku,
                    product.Description,
                    product.Price,
                    Status = (int)product.Status,
                    product.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new DbConcurrencyException(
                $"Product '{product.Id}' was not updated. It has been deleted, or it belongs to another tenant.");
        }
    }

    private const string DeleteSql = """
        UPDATE dbo.Products
        SET
            Status    = @ArchivedStatus,
            DeletedAt = SYSUTCDATETIME(),
            UpdatedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId
          AND Id       = @ProductId
          AND DeletedAt IS NULL;
        """;

    /// <remarks>
    /// A soft delete. Order items reference the product row for their historical
    /// SKU and name, so removing it would corrupt past orders.
    /// </remarks>
    public async Task DeleteAsync(
        Guid tenantId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                DeleteSql,
                new
                {
                    TenantId = tenantId,
                    ProductId = productId,
                    ArchivedStatus = (int)ProductStatus.Archived,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Row shape of <see cref="GetByIdSql"/>.</summary>
    private sealed class ProductRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid ShopId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Sku { get; init; } = string.Empty;

        public string? Description { get; init; }

        public decimal Price { get; init; }

        public string CurrencyCode { get; init; } = string.Empty;

        public int Status { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public DateTime? DeletedAt { get; init; }

        public Product ToDomain()
        {
            return Product.Restore(
                id: Id,
                tenantId: TenantId,
                shopId: ShopId,
                name: Name,
                sku: Sku,
                description: Description,
                price: Price,
                currencyCode: CurrencyCode,
                status: (ProductStatus)Status,
                createdAt: CreatedAt,
                updatedAt: UpdatedAt,
                deletedAt: DeletedAt);
        }
    }
}
