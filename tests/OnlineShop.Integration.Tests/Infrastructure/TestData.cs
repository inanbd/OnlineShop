using Dapper;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Ordering;
using OnlineShop.Domain.Shops;

namespace OnlineShop.Integration.Tests.Infrastructure;

/// <summary>Identifiers produced by <see cref="TestData.SeedAsync"/>.</summary>
public sealed record SeededShop(
    Guid TenantId,
    Guid ShopId,
    Guid CustomerId,
    Guid ProductAId,
    Guid ProductBId,
    Guid CartId,
    Guid OwnerUserId);

/// <summary>
/// Inserts a tenant with one shop, one customer, two products with stock, and
/// an open cart, writing straight to a chosen database.
/// </summary>
/// <remarks>
/// Seeding deliberately bypasses the application. Tests that assert where a row
/// landed need to place rows precisely, including into the replica, which no
/// command handler is able to do.
/// </remarks>
public static class TestData
{
    public const string Currency = "USD";

    public static async Task<SeededShop> SeedAsync(
        SqlServerFixture fixture,
        string database,
        Guid? tenantId = null,
        int stockA = 10,
        int stockB = 5,
        int cartQuantityA = 2,
        decimal priceA = 25.00m,
        decimal priceB = 40.00m)
    {
        var seeded = new SeededShop(
            TenantId: tenantId ?? Guid.NewGuid(),
            ShopId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            ProductAId: Guid.NewGuid(),
            ProductBId: Guid.NewGuid(),
            CartId: Guid.NewGuid(),
            OwnerUserId: Guid.NewGuid());

        await using var connection = await fixture.OpenAsync(database);

        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.Tenants (Id, Name, Slug)
            VALUES (@TenantId, @TenantName, @TenantSlug);

            INSERT INTO dbo.Shops (Id, TenantId, Name, Slug, CurrencyCode, IsActive)
            VALUES (@ShopId, @TenantId, N'Test Shop', @ShopSlug, @Currency, 1);

            INSERT INTO dbo.Customers (Id, TenantId, ShopId, Email, FullName)
            VALUES (@CustomerId, @TenantId, @ShopId, @CustomerEmail, N'Ada Lovelace');

            INSERT INTO dbo.Products (Id, TenantId, ShopId, Name, Sku, Price, CurrencyCode, Status)
            VALUES
                (@ProductAId, @TenantId, @ShopId, N'Widget A', @SkuA, @PriceA, @Currency, @ActiveStatus),
                (@ProductBId, @TenantId, @ShopId, N'Widget B', @SkuB, @PriceB, @Currency, @ActiveStatus);

            INSERT INTO dbo.InventoryItems (Id, TenantId, ShopId, ProductId, QuantityOnHand, QuantityReserved, ReorderThreshold)
            VALUES
                (NEWID(), @TenantId, @ShopId, @ProductAId, @StockA, 0, 2),
                (NEWID(), @TenantId, @ShopId, @ProductBId, @StockB, 0, 2);

            INSERT INTO dbo.Carts (Id, TenantId, ShopId, CustomerId, Status)
            VALUES (@CartId, @TenantId, @ShopId, @CustomerId, @OpenCart);

            INSERT INTO dbo.CartItems (Id, TenantId, CartId, ProductId, Quantity, UnitPrice)
            VALUES (NEWID(), @TenantId, @CartId, @ProductAId, @CartQuantityA, @PriceA);
            """,
            new
            {
                seeded.TenantId,
                seeded.ShopId,
                seeded.CustomerId,
                seeded.ProductAId,
                seeded.ProductBId,
                seeded.CartId,
                TenantName = $"Tenant {seeded.TenantId:N}"[..20],
                TenantSlug = $"t-{seeded.TenantId:N}"[..12],
                ShopSlug = $"s-{seeded.ShopId:N}"[..12],
                CustomerEmail = $"ada-{seeded.CustomerId:N}@example.test",
                SkuA = $"SKU-A-{seeded.ProductAId:N}"[..16],
                SkuB = $"SKU-B-{seeded.ProductBId:N}"[..16],
                PriceA = priceA,
                PriceB = priceB,
                Currency,
                StockA = stockA,
                StockB = stockB,
                CartQuantityA = cartQuantityA,
                ActiveStatus = (int)ProductStatus.Active,
                OpenCart = (int)CartStatus.Open,
            });

        return seeded;
    }

    /// <summary>Adds a second open cart for the same customer.</summary>
    public static async Task<Guid> AddCartAsync(
        SqlServerFixture fixture,
        string database,
        SeededShop shop,
        Guid productId,
        int quantity,
        decimal unitPrice)
    {
        var cartId = Guid.NewGuid();

        await using var connection = await fixture.OpenAsync(database);

        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.Carts (Id, TenantId, ShopId, CustomerId, Status)
            VALUES (@CartId, @TenantId, @ShopId, @CustomerId, @OpenCart);

            INSERT INTO dbo.CartItems (Id, TenantId, CartId, ProductId, Quantity, UnitPrice)
            VALUES (NEWID(), @TenantId, @CartId, @ProductId, @Quantity, @UnitPrice);
            """,
            new
            {
                CartId = cartId,
                shop.TenantId,
                shop.ShopId,
                shop.CustomerId,
                ProductId = productId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                OpenCart = (int)CartStatus.Open,
            });

        return cartId;
    }

    public static async Task AddShopMemberAsync(
        SqlServerFixture fixture,
        string database,
        SeededShop shop,
        string email,
        ShopMemberRole role,
        ShopMemberStatus status)
    {
        await using var connection = await fixture.OpenAsync(database);

        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.ShopMembers
                (Id, TenantId, ShopId, Email, Role, Status, InvitedByUserId, InvitedAt)
            VALUES
                (NEWID(), @TenantId, @ShopId, @Email, @Role, @Status, @OwnerUserId, SYSUTCDATETIME());
            """,
            new
            {
                shop.TenantId,
                shop.ShopId,
                shop.OwnerUserId,
                Email = email.ToLowerInvariant(),
                Role = (int)role,
                Status = (int)status,
            });
    }
}

/// <summary>Reads straight from a database, for assertions the application cannot make.</summary>
public static class Db
{
    public static async Task<T?> ScalarAsync<T>(
        SqlServerFixture fixture,
        string database,
        string sql,
        object? parameters = null)
    {
        await using var connection = await fixture.OpenAsync(database);
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    public static async Task<int> CountAsync(
        SqlServerFixture fixture,
        string database,
        string table,
        string? whereClause = null,
        object? parameters = null)
    {
        var where = whereClause is null ? string.Empty : $" WHERE {whereClause}";
        return await ScalarAsync<int>(fixture, database, $"SELECT COUNT(1) FROM dbo.[{table}]{where};", parameters);
    }

    public static async Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlServerFixture fixture,
        string database,
        string sql,
        object? parameters = null)
    {
        await using var connection = await fixture.OpenAsync(database);
        return (await connection.QueryAsync<T>(sql, parameters)).ToList();
    }

    public static async Task ExecuteAsync(
        SqlServerFixture fixture,
        string database,
        string sql,
        object? parameters = null)
    {
        await using var connection = await fixture.OpenAsync(database);
        await connection.ExecuteAsync(sql, parameters);
    }
}
