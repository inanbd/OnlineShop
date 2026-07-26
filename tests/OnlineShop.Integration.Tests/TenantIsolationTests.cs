using Microsoft.Data.SqlClient;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Dashboard.Queries.GetShopDashboard;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Application.Orders.Queries.GetOrders;
using OnlineShop.Application.Products.Commands.DeleteProduct;
using OnlineShop.Application.Products.Commands.UpdateProduct;
using OnlineShop.Application.Products.Queries.GetProductById;
using OnlineShop.Application.Products.Queries.GetProducts;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Two tenants sharing every table, with real data in both.
/// </summary>
/// <remarks>
/// Each test runs as tenant A and reaches for something belonging to tenant B.
/// A row in another tenant is indistinguishable from a row that does not exist,
/// which is deliberate: it stops a caller probing for other tenants'
/// identifiers.
/// </remarks>
public sealed class TenantIsolationTests : IntegrationTest
{
    public TenantIsolationTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task Another_tenants_product_cannot_be_read()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();
        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(new GetProductByIdQuery(tenantB.ProductAId)));

        // The row is genuinely there; it is the tenant filter that hides it.
        Assert.Equal(1, await Db.CountAsync(
            Fixture, Primary, "Products", "Id = @id", new { id = tenantB.ProductAId }));
    }

    [SkippableFact]
    public async Task Listings_return_only_the_current_tenants_rows()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        Assert.Equal(4, await Db.CountAsync(Fixture, Primary, "Products"));

        await using var harnessA = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);
        var pageA = await harnessA.SendAsync(new GetProductsQuery(new ProductFilter()));

        Assert.Equal(2, pageA.TotalCount);
        Assert.All(pageA.Items, item => Assert.Equal(tenantA.ShopId, item.ShopId));

        await using var harnessB = TestHarness.SinglePrimary(Fixture, tenantB.TenantId);
        var pageB = await harnessB.SendAsync(new GetProductsQuery(new ProductFilter()));

        Assert.Equal(2, pageB.TotalCount);
        Assert.All(pageB.Items, item => Assert.Equal(tenantB.ShopId, item.ShopId));
    }

    [SkippableFact]
    public async Task Another_tenants_product_cannot_be_updated()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();
        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(new UpdateProductCommand(
                tenantB.ProductAId, "Hijacked", "HIJACK-1", null, 0.01m)));

        var name = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT Name FROM dbo.Products WHERE Id = @id;", new { id = tenantB.ProductAId });

        Assert.Equal("Widget A", name);
    }

    [SkippableFact]
    public async Task Another_tenants_product_cannot_be_deleted()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();
        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(new DeleteProductCommand(tenantB.ProductAId)));

        var deletedAt = await Db.ScalarAsync<DateTime?>(
            Fixture, Primary, "SELECT DeletedAt FROM dbo.Products WHERE Id = @id;", new { id = tenantB.ProductAId });

        Assert.Null(deletedAt);
    }

    [SkippableFact]
    public async Task Another_tenants_cart_cannot_be_checked_out()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();
        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(new PlaceOrderCommand(
                tenantB.ShopId, tenantB.CustomerId, tenantB.CartId, "stripe", "pi_cross_tenant")));

        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "Orders"));
    }

    [SkippableFact]
    public async Task Another_tenants_dashboard_is_not_found()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();
        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(new GetShopDashboardQuery(tenantB.ShopId)));
    }

    [SkippableFact]
    public async Task Orders_are_never_visible_across_tenants()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        await using var harnessB = TestHarness.SinglePrimary(Fixture, tenantB.TenantId);
        await harnessB.SendAsync(new PlaceOrderCommand(
            tenantB.ShopId, tenantB.CustomerId, tenantB.CartId, "stripe", "pi_tenant_b"));

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Orders"));

        await using var harnessA = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);
        var ordersForA = await harnessA.SendAsync(new GetOrdersQuery(new OrderFilter()));

        Assert.Equal(0, ordersForA.TotalCount);
        Assert.Empty(ordersForA.Items);
    }

    [SkippableFact]
    public async Task An_empty_tenant_is_rejected_before_any_sql_runs()
    {
        RequireDatabase();

        var (tenantA, _) = await SeedTwoTenantsAsync();

        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);
        harness.TenantContext.TenantId = Guid.Empty;

        // An empty GUID is a perfectly valid parameter that would simply match
        // nothing. TenantGuard turns it into an obvious failure instead.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SendAsync(new GetProductsQuery(new ProductFilter())));

        Assert.Contains("without a tenant", failure.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task The_database_rejects_a_product_pointing_at_another_tenants_shop()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        // The composite foreign key is (TenantId, ShopId) -> (TenantId, Id), so
        // a cross-tenant reference cannot be stored even by a statement that
        // bypasses the application entirely.
        var failure = await Assert.ThrowsAsync<SqlException>(() => Db.ExecuteAsync(
            Fixture, Primary,
            """
            INSERT INTO dbo.Products (Id, TenantId, ShopId, Name, Sku, Price, CurrencyCode, Status)
            VALUES (NEWID(), @TenantId, @ForeignShopId, N'Smuggled', 'SMUGGLE-1', 1.00, 'USD', 1);
            """,
            new { tenantA.TenantId, ForeignShopId = tenantB.ShopId }));

        Assert.Contains("FK_Products_Shops", failure.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task The_database_rejects_an_order_item_pointing_at_another_tenants_order()
    {
        RequireDatabase();

        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        await using var harnessB = TestHarness.SinglePrimary(Fixture, tenantB.TenantId);
        var placed = await harnessB.SendAsync(new PlaceOrderCommand(
            tenantB.ShopId, tenantB.CustomerId, tenantB.CartId, "stripe", "pi_b"));

        var failure = await Assert.ThrowsAsync<SqlException>(() => Db.ExecuteAsync(
            Fixture, Primary,
            """
            INSERT INTO dbo.OrderItems
                (Id, TenantId, OrderId, ProductId, Sku, ProductName, Quantity, UnitPrice, LineTotal)
            VALUES
                (NEWID(), @TenantId, @ForeignOrderId, @ProductId, 'X', N'X', 1, 1.00, 1.00);
            """,
            new { tenantA.TenantId, ForeignOrderId = placed.OrderId, ProductId = tenantA.ProductAId }));

        Assert.Contains("FK_OrderItems_Orders", failure.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Two_tenants_may_reuse_the_same_sku_and_slug()
    {
        RequireDatabase();

        // Uniqueness is scoped per tenant, not global. Two unrelated merchants
        // must both be able to use SKU "WIDGET-1".
        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);

        await Db.ExecuteAsync(
            Fixture, Primary,
            "UPDATE dbo.Products SET Sku = 'SHARED-SKU' WHERE Id = @id;",
            new { id = tenantA.ProductAId });

        await Db.ExecuteAsync(
            Fixture, Primary,
            "UPDATE dbo.Products SET Sku = 'SHARED-SKU' WHERE Id = @id;",
            new { id = tenantB.ProductAId });

        Assert.Equal(2, await Db.CountAsync(
            Fixture, Primary, "Products", "Sku = 'SHARED-SKU'"));
    }

    [SkippableFact]
    public async Task One_shop_cannot_reuse_a_sku_within_the_same_tenant()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);

        var failure = await Assert.ThrowsAsync<SqlException>(() => Db.ExecuteAsync(
            Fixture, Primary,
            """
            UPDATE dbo.Products
            SET Sku = (SELECT Sku FROM dbo.Products WHERE Id = @ProductAId)
            WHERE Id = @ProductBId;
            """,
            new { shop.ProductAId, shop.ProductBId }));

        Assert.Contains("UX_Products_Tenant_Shop_Sku", failure.Message, StringComparison.Ordinal);
    }

    private async Task<(SeededShop TenantA, SeededShop TenantB)> SeedTwoTenantsAsync()
    {
        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);
        return (tenantA, tenantB);
    }
}
