using Microsoft.Data.SqlClient;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Contracts.Customers;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Dashboard.Queries.GetShopDashboard;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Application.Orders.Queries.GetOrders;
using OnlineShop.Application.Products.Commands.CreateProduct;
using OnlineShop.Application.Products.Queries.GetProductById;
using OnlineShop.Application.Products.Queries.GetProducts;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Runs the read path against a database SQL Server has been told is read-only.
/// </summary>
/// <remarks>
/// A real readable secondary rejects writes at the server. Setting the replica
/// database to READ_ONLY reproduces that exactly, so if any query in this
/// codebase performed a write — an audit insert, a temp table, a cached
/// counter update — these tests would fail rather than pass quietly and break
/// the first time a real replica appeared.
/// </remarks>
public sealed class ReadOnlyReplicaTests : IntegrationTest
{
    public ReadOnlyReplicaTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task Every_query_succeeds_against_a_read_only_replica()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, stockB: 3, cartQuantityA: 2);

        await using (var writeHarness = TestHarness.SinglePrimary(Fixture, shop.TenantId))
        {
            // Inside the dashboard's window, which ends at the wall clock.
            writeHarness.Clock.Set(DateTime.UtcNow.AddMinutes(-30));

            await writeHarness.SendAsync(new PlaceOrderCommand(
                shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_readonly"));
        }

        await Fixture.ReplicateAsync();
        await Fixture.SetReplicaReadOnlyAsync(true);

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        // Every read model, against a database that physically cannot be written.
        var products = await harness.SendAsync(new GetProductsQuery(new ProductFilter()));
        Assert.Equal(2, products.TotalCount);

        var product = await harness.SendAsync(new GetProductByIdQuery(shop.ProductAId));
        Assert.Equal("Widget A", product.Name);

        var orders = await harness.SendAsync(new GetOrdersQuery(new OrderFilter()));
        Assert.Equal(1, orders.TotalCount);

        var dashboard = await harness.SendAsync(new GetShopDashboardQuery(shop.ShopId));
        Assert.Equal(1, dashboard.OrdersInWindow);
        Assert.Equal(50.00m, dashboard.RevenueInWindow);

        var customers = await harness
            .GetRequiredService<Application.Abstractions.Persistence.Queries.ICustomerQueries>()
            .GetAsync(shop.TenantId, new CustomerFilter());
        Assert.Single(customers);
    }

    [SkippableFact]
    public async Task A_write_aimed_at_the_replica_is_refused_by_the_server()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();
        await Fixture.SetReplicaReadOnlyAsync(true);

        var failure = await Assert.ThrowsAsync<SqlException>(() => Db.ExecuteAsync(
            Fixture, Replica,
            "UPDATE dbo.Products SET Name = N'Should Not Work' WHERE Id = @id;",
            new { id = shop.ProductAId }));

        Assert.Contains("read-only", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Commands_keep_working_while_the_replica_is_read_only()
    {
        RequireDatabase();

        // Writes go to the primary, so a read-only replica must not affect them.
        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();
        await Fixture.SetReplicaReadOnlyAsync(true);

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var productId = await harness.SendAsync(new CreateProductCommand(
            ShopId: shop.ShopId,
            Name: "Written While Replica Locked",
            Sku: "RO-001",
            Description: null,
            Price: 3.50m,
            InitialQuantityOnHand: 4));

        Assert.Equal(1, await Db.CountAsync(
            Fixture, Primary, "Products", "Id = @productId", new { productId }));

        // And the strongly consistent read still reaches the primary.
        var strong = await harness.SendAsync(new GetProductByIdQuery(productId, ReadConsistency.Strong));
        Assert.Equal("Written While Replica Locked", strong.Name);
    }

    /// <summary>
    /// Returns the replica to a writable state, otherwise the next test's reset
    /// would fail against a read-only database.
    /// </summary>
    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            await Fixture.SetReplicaReadOnlyAsync(false);
        }

        await base.DisposeAsync();
    }
}
