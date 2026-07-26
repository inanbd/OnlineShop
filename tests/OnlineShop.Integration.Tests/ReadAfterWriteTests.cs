using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Application.Orders.Queries.GetOrderDetails;
using OnlineShop.Application.Products.Commands.CreateProduct;
using OnlineShop.Application.Products.Queries.GetProductById;
using OnlineShop.Domain.Ordering;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// The read-after-write problem, reproduced and then solved.
/// </summary>
/// <remarks>
/// With the replica deliberately never caught up, a read issued straight after
/// a write behaves exactly as it would behind real replication lag — except the
/// lag here is unbounded, so the failure is deterministic instead of a race
/// that shows up once a week in production.
/// </remarks>
public sealed class ReadAfterWriteTests : IntegrationTest
{
    public ReadAfterWriteTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task A_product_read_back_eventually_is_missing_but_strongly_is_found()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var productId = await harness.SendAsync(new CreateProductCommand(
            ShopId: shop.ShopId,
            Name: "Just Created",
            Sku: "RAW-001",
            Description: null,
            Price: 19.99m,
            PublishImmediately: true));

        // 1. The default read goes to the replica, which is behind. This is the
        //    404-on-a-row-you-just-saved that the strategy exists to handle.
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(new GetProductByIdQuery(productId)));

        // 2. The documented escape hatch routes this one read to the primary.
        var strong = await harness.SendAsync(
            new GetProductByIdQuery(productId, ReadConsistency.Strong));

        Assert.Equal(productId, strong.Id);
        Assert.Equal("Just Created", strong.Name);

        // 3. Once replication catches up, the ordinary path works.
        await Fixture.ReplicateAsync();

        var eventual = await harness.SendAsync(new GetProductByIdQuery(productId));
        Assert.Equal(productId, eventual.Id);
    }

    [SkippableFact]
    public async Task The_order_confirmation_page_can_read_the_order_it_just_created()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new PlaceOrderCommand(
            ShopId: shop.ShopId,
            CustomerId: shop.CustomerId,
            CartId: shop.CartId,
            PaymentProvider: "test-provider",
            PaymentReference: $"ref-{Guid.NewGuid():N}"));

        // Redirecting a shopper to a confirmation page that cannot find their
        // order is not an acceptable outcome, which is why this query carries a
        // strong-consistency allowance.
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(new GetOrderDetailsQuery(placed.OrderId)));

        var confirmation = await harness.SendAsync(
            new GetOrderDetailsQuery(placed.OrderId, ReadConsistency.Strong));

        Assert.Equal(placed.OrderId, confirmation.Id);
        Assert.Equal(placed.OrderNumber, confirmation.OrderNumber);
        Assert.Equal(OrderStatus.Pending, confirmation.Status);
        Assert.Single(confirmation.Items);
        Assert.Single(confirmation.Payments);
        Assert.Single(confirmation.StatusHistory);
    }

    [SkippableFact]
    public async Task Strong_consistency_returns_exactly_what_the_primary_holds()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        // Make the two databases disagree about the same row, then check which
        // value each consistency level reports.
        await Db.ExecuteAsync(
            Fixture, Primary,
            "UPDATE dbo.Products SET Price = 111.00 WHERE Id = @id;",
            new { id = shop.ProductAId });

        await Db.ExecuteAsync(
            Fixture, Replica,
            "UPDATE dbo.Products SET Price = 222.00 WHERE Id = @id;",
            new { id = shop.ProductAId });

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var eventual = await harness.SendAsync(new GetProductByIdQuery(shop.ProductAId));
        var strong = await harness.SendAsync(
            new GetProductByIdQuery(shop.ProductAId, ReadConsistency.Strong));

        Assert.Equal(222.00m, eventual.Price);
        Assert.Equal(111.00m, strong.Price);
    }

    [SkippableFact]
    public async Task A_command_reads_the_primary_even_when_the_replica_disagrees()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        // The replica says the product costs 999; the primary says 25. The
        // command loads through its transaction, so it must use the primary's
        // value. Pricing an order from a stale replica read would be a genuine
        // correctness bug.
        await Db.ExecuteAsync(
            Fixture, Replica,
            "UPDATE dbo.Products SET Price = 999.00 WHERE Id = @id;",
            new { id = shop.ProductAId });

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new PlaceOrderCommand(
            ShopId: shop.ShopId,
            CustomerId: shop.CustomerId,
            CartId: shop.CartId,
            PaymentProvider: "test-provider",
            PaymentReference: $"ref-{Guid.NewGuid():N}"));

        // Seeded cart holds 2 of Widget A at the primary's price of 25.00.
        Assert.Equal(50.00m, placed.GrandTotal);

        var storedTotal = await Db.ScalarAsync<decimal>(
            Fixture, Primary,
            "SELECT GrandTotal FROM dbo.Orders WHERE Id = @id;",
            new { id = placed.OrderId });

        Assert.Equal(50.00m, storedTotal);
    }
}
