using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Dashboard.Queries.GetShopDashboard;
using OnlineShop.Application.Products.Commands.CreateProduct;
using OnlineShop.Application.Products.Commands.UpdateProduct;
using OnlineShop.Application.Products.Queries.GetProducts;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Proves the routing empirically instead of trusting it.
/// </summary>
/// <remarks>
/// The primary and the replica are two separate databases with nothing
/// synchronising them, so "which connection did this use" becomes an
/// observable fact: a row written by a command appears in one and not the
/// other, and a row that exists only in the replica is visible only to reads
/// that genuinely went there.
/// </remarks>
public sealed class ConnectionRoutingTests : IntegrationTest
{
    public ConnectionRoutingTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task A_command_writes_to_the_primary_and_leaves_the_replica_alone()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        // Both databases start identical, so any divergence is caused by the command.
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "Products"));
        Assert.Equal(2, await Db.CountAsync(Fixture, Replica, "Products"));

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var productId = await harness.SendAsync(new CreateProductCommand(
            ShopId: shop.ShopId,
            Name: "Routed Product",
            Sku: "ROUTE-001",
            Description: null,
            Price: 12.34m,
            InitialQuantityOnHand: 7));

        Assert.Equal(3, await Db.CountAsync(Fixture, Primary, "Products"));
        Assert.Equal(2, await Db.CountAsync(Fixture, Replica, "Products"));

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Products", "Id = @productId", new { productId }));
        Assert.Equal(0, await Db.CountAsync(Fixture, Replica, "Products", "Id = @productId", new { productId }));
    }

    [SkippableFact]
    public async Task A_query_reads_from_the_replica()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        // A row that exists only on the replica. No command could have put it
        // there, so seeing it proves the read went to the replica.
        var replicaOnlyId = Guid.NewGuid();
        await Db.ExecuteAsync(
            Fixture,
            Replica,
            """
            INSERT INTO dbo.Products (Id, TenantId, ShopId, Name, Sku, Price, CurrencyCode, Status)
            VALUES (@Id, @TenantId, @ShopId, N'Replica Only', 'REPLICA-ONLY', 9.99, 'USD', 1);
            """,
            new { Id = replicaOnlyId, shop.TenantId, shop.ShopId });

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var page = await harness.SendAsync(new GetProductsQuery(new ProductFilter { ShopId = shop.ShopId }));

        Assert.Equal(3, page.TotalCount);
        Assert.Contains(page.Items, item => item.Id == replicaOnlyId);
    }

    [SkippableFact]
    public async Task A_query_does_not_see_rows_that_exist_only_on_the_primary()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);

        // The replica was never seeded; it has nothing at all.
        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var page = await harness.SendAsync(new GetProductsQuery(new ProductFilter { ShopId = shop.ShopId }));

        Assert.Equal(0, page.TotalCount);
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "Products"));
    }

    [SkippableFact]
    public async Task Dashboard_analytics_are_served_from_the_replica()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        // Give the replica a product the primary does not have, then check the
        // dashboard's active-product counter reflects the replica.
        await Db.ExecuteAsync(
            Fixture,
            Replica,
            """
            INSERT INTO dbo.Products (Id, TenantId, ShopId, Name, Sku, Price, CurrencyCode, Status)
            VALUES (NEWID(), @TenantId, @ShopId, N'Replica Extra', 'REPLICA-EXTRA', 1.00, 'USD', 1);
            """,
            new { shop.TenantId, shop.ShopId });

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var dashboard = await harness.SendAsync(new GetShopDashboardQuery(shop.ShopId));

        Assert.Equal(3, dashboard.ActiveProductCount);
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "Products"));
    }

    [SkippableFact]
    public async Task An_update_command_changes_the_primary_only()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        await harness.SendAsync(new UpdateProductCommand(
            ProductId: shop.ProductAId,
            Name: "Renamed On Primary",
            Sku: "SKU-RENAMED",
            Description: "changed",
            Price: 99.95m));

        var primaryName = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT Name FROM dbo.Products WHERE Id = @id;", new { id = shop.ProductAId });

        var replicaName = await Db.ScalarAsync<string>(
            Fixture, Replica, "SELECT Name FROM dbo.Products WHERE Id = @id;", new { id = shop.ProductAId });

        Assert.Equal("Renamed On Primary", primaryName);
        Assert.Equal("Widget A", replicaName);
    }

    [SkippableFact]
    public async Task Reads_catch_up_once_replication_runs()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        await harness.SendAsync(new CreateProductCommand(
            ShopId: shop.ShopId,
            Name: "Eventually Visible",
            Sku: "EVENTUAL-001",
            Description: null,
            Price: 5.00m,
            PublishImmediately: true));

        var beforeReplication = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { ShopId = shop.ShopId }));
        Assert.Equal(2, beforeReplication.TotalCount);

        await Fixture.ReplicateAsync();

        var afterReplication = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { ShopId = shop.ShopId }));
        Assert.Equal(3, afterReplication.TotalCount);
        Assert.Contains(afterReplication.Items, item => item.Name == "Eventually Visible");
    }
}
