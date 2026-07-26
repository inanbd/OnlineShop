using System.Data;
using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Customers;
using OnlineShop.Domain.Ordering;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Repositories;

/// <summary>
/// Customer and cart write repository.
/// </summary>
/// <remarks>
/// <code>
/// CustomerRepository -> the caller's transaction -> WriteConnection -> primary
/// </code>
/// </remarks>
internal sealed class CustomerRepository : ICustomerRepository
{
    private const string GetByIdSql = """
        SELECT
            c.Id,
            c.TenantId,
            c.ShopId,
            c.Email,
            c.FullName,
            c.CreatedAt,
            c.UpdatedAt
        FROM dbo.Customers AS c
        WHERE c.TenantId = @TenantId
          AND c.Id       = @CustomerId;
        """;

    public async Task<Customer?> GetByIdAsync(
        Guid tenantId,
        Guid customerId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<CustomerRow>(
            new CommandDefinition(
                GetByIdSql,
                new { TenantId = tenantId, CustomerId = customerId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    private const string InsertSql = """
        INSERT INTO dbo.Customers
        (
            Id,
            TenantId,
            ShopId,
            Email,
            FullName,
            CreatedAt,
            UpdatedAt
        )
        VALUES
        (
            @Id,
            @TenantId,
            @ShopId,
            @Email,
            @FullName,
            @CreatedAt,
            @UpdatedAt
        );
        """;

    public async Task InsertAsync(
        Customer customer,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        TenantGuard.Require(customer.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                new
                {
                    customer.Id,
                    customer.TenantId,
                    customer.ShopId,
                    customer.Email,
                    customer.FullName,
                    customer.CreatedAt,
                    customer.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string UpdateSql = """
        UPDATE dbo.Customers
        SET
            FullName  = @FullName,
            UpdatedAt = @UpdatedAt
        WHERE TenantId = @TenantId
          AND Id       = @Id;
        """;

    public async Task UpdateAsync(
        Customer customer,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        TenantGuard.Require(customer.TenantId);

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                UpdateSql,
                new
                {
                    customer.Id,
                    customer.TenantId,
                    customer.FullName,
                    customer.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new DbConcurrencyException(
                $"Customer '{customer.Id}' was not updated. They do not exist in this tenant.");
        }
    }

    private const string GetOpenCartSql = """
        SELECT TOP (1)
            c.Id,
            c.TenantId,
            c.ShopId,
            c.CustomerId,
            c.Status,
            c.CreatedAt,
            c.UpdatedAt
        FROM dbo.Carts AS c
        WHERE c.TenantId   = @TenantId
          AND c.ShopId     = @ShopId
          AND c.CustomerId = @CustomerId
          AND c.Status     = @OpenStatus
        ORDER BY c.CreatedAt DESC;
        """;

    public async Task<Cart?> GetOpenCartAsync(
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<CartRow>(
            new CommandDefinition(
                GetOpenCartSql,
                new
                {
                    TenantId = tenantId,
                    ShopId = shopId,
                    CustomerId = customerId,
                    OpenStatus = (int)CartStatus.Open,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var items = await GetCartItemsAsync(tenantId, row.Id, transaction, cancellationToken)
            .ConfigureAwait(false);

        return row.ToDomain(items);
    }

    private const string GetCartByIdSql = """
        SELECT
            c.Id,
            c.TenantId,
            c.ShopId,
            c.CustomerId,
            c.Status,
            c.CreatedAt,
            c.UpdatedAt
        FROM dbo.Carts AS c
        WHERE c.TenantId = @TenantId
          AND c.Id       = @CartId;
        """;

    public async Task<Cart?> GetCartByIdAsync(
        Guid tenantId,
        Guid cartId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<CartRow>(
            new CommandDefinition(
                GetCartByIdSql,
                new { TenantId = tenantId, CartId = cartId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var items = await GetCartItemsAsync(tenantId, row.Id, transaction, cancellationToken)
            .ConfigureAwait(false);

        return row.ToDomain(items);
    }

    private const string GetCartItemsSql = """
        SELECT
            ci.Id,
            ci.TenantId,
            ci.CartId,
            ci.ProductId,
            ci.Quantity,
            ci.UnitPrice
        FROM dbo.CartItems AS ci
        WHERE ci.TenantId = @TenantId
          AND ci.CartId   = @CartId
        ORDER BY ci.Id ASC;
        """;

    private static async Task<List<CartItem>> GetCartItemsAsync(
        Guid tenantId,
        Guid cartId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = transaction.RequireConnection();

        var rows = await connection.QueryAsync<CartItemRow>(
            new CommandDefinition(
                GetCartItemsSql,
                new { TenantId = tenantId, CartId = cartId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <remarks>
    /// The <c>Status = @OpenStatus</c> predicate makes this idempotent under
    /// concurrency: a second checkout for the same cart affects no rows and is
    /// reported as a conflict rather than producing a duplicate order.
    /// </remarks>
    private const string MarkCartCheckedOutSql = """
        UPDATE dbo.Carts
        SET
            Status    = @CheckedOutStatus,
            UpdatedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId
          AND Id       = @CartId
          AND Status   = @OpenStatus;
        """;

    public async Task MarkCartCheckedOutAsync(
        Guid tenantId,
        Guid cartId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                MarkCartCheckedOutSql,
                new
                {
                    TenantId = tenantId,
                    CartId = cartId,
                    CheckedOutStatus = (int)CartStatus.CheckedOut,
                    OpenStatus = (int)CartStatus.Open,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new DbConcurrencyException(
                $"Cart '{cartId}' was not checked out. It has already been checked out or abandoned, " +
                "which usually means a concurrent checkout won the race.");
        }
    }

    private const string InsertCartSql = """
        INSERT INTO dbo.Carts (Id, TenantId, ShopId, CustomerId, Status, CreatedAt, UpdatedAt)
        VALUES (@Id, @TenantId, @ShopId, @CustomerId, @Status, @CreatedAt, @UpdatedAt);
        """;

    public async Task InsertCartAsync(
        Cart cart,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cart);
        TenantGuard.Require(cart.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertCartSql,
                new
                {
                    cart.Id,
                    cart.TenantId,
                    cart.ShopId,
                    cart.CustomerId,
                    Status = (int)cart.Status,
                    cart.CreatedAt,
                    cart.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <remarks>
    /// One statement rather than a read followed by a branch: the unique index
    /// on (TenantId, CartId, ProductId) means two concurrent adds of the same
    /// product would otherwise race, and the loser would fail on a duplicate key
    /// instead of merging.
    /// </remarks>
    private const string UpsertCartItemSql = """
        MERGE dbo.CartItems WITH (HOLDLOCK) AS target
        USING
        (
            SELECT @TenantId AS TenantId, @CartId AS CartId, @ProductId AS ProductId
        ) AS source
            ON  target.TenantId  = source.TenantId
            AND target.CartId    = source.CartId
            AND target.ProductId = source.ProductId
        WHEN MATCHED THEN
            UPDATE SET Quantity = @Quantity, UnitPrice = @UnitPrice
        WHEN NOT MATCHED THEN
            INSERT (Id, TenantId, CartId, ProductId, Quantity, UnitPrice)
            VALUES (NEWID(), source.TenantId, source.CartId, source.ProductId, @Quantity, @UnitPrice);
        """;

    public async Task UpsertCartItemAsync(
        Guid tenantId,
        Guid cartId,
        Guid productId,
        int quantity,
        decimal unitPrice,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Use RemoveCartItemAsync to take a line out of the cart.");
        }

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                UpsertCartItemSql,
                new
                {
                    TenantId = tenantId,
                    CartId = cartId,
                    ProductId = productId,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string RemoveCartItemSql = """
        DELETE FROM dbo.CartItems
        WHERE TenantId  = @TenantId
          AND CartId    = @CartId
          AND ProductId = @ProductId;
        """;

    public async Task RemoveCartItemAsync(
        Guid tenantId,
        Guid cartId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                RemoveCartItemSql,
                new { TenantId = tenantId, CartId = cartId, ProductId = productId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Row shape of <see cref="GetByIdSql"/>.</summary>
    private sealed class CustomerRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid ShopId { get; init; }

        public string Email { get; init; } = string.Empty;

        public string FullName { get; init; } = string.Empty;

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public Customer ToDomain()
        {
            return Customer.Restore(
                id: Id,
                tenantId: TenantId,
                shopId: ShopId,
                email: Email,
                fullName: FullName,
                createdAt: CreatedAt,
                updatedAt: UpdatedAt);
        }
    }

    /// <summary>Row shape of <see cref="GetCartByIdSql"/>.</summary>
    private sealed class CartRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid ShopId { get; init; }

        public Guid CustomerId { get; init; }

        public int Status { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public Cart ToDomain(IEnumerable<CartItem> items)
        {
            return Cart.Restore(
                id: Id,
                tenantId: TenantId,
                shopId: ShopId,
                customerId: CustomerId,
                status: (CartStatus)Status,
                items: items,
                createdAt: CreatedAt,
                updatedAt: UpdatedAt);
        }
    }

    /// <summary>Row shape of <see cref="GetCartItemsSql"/>.</summary>
    private sealed class CartItemRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid CartId { get; init; }

        public Guid ProductId { get; init; }

        public int Quantity { get; init; }

        public decimal UnitPrice { get; init; }

        public CartItem ToDomain()
        {
            return CartItem.Restore(
                id: Id,
                tenantId: TenantId,
                cartId: CartId,
                productId: ProductId,
                quantity: Quantity,
                unitPrice: UnitPrice);
        }
    }
}
