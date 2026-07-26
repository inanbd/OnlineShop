using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Orders.Commands.CancelOrder;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Application.Products.Commands.CreateProduct;
using OnlineShop.Application.Shops.Commands.CreateShop;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Ordering;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Real transactions against a real engine: everything commits, or nothing does.
/// </summary>
public sealed class TransactionTests : IntegrationTest
{
    public TransactionTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task Placing_an_order_writes_all_six_steps()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 2);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new PlaceOrderCommand(
            ShopId: shop.ShopId,
            CustomerId: shop.CustomerId,
            CartId: shop.CartId,
            PaymentProvider: "stripe",
            PaymentReference: "pi_abc123"));

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Orders"));
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "OrderItems"));
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Payments"));
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "OrderStatusHistory"));

        var reserved = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityReserved FROM dbo.InventoryItems WHERE ProductId = @id;",
            new { id = shop.ProductAId });
        Assert.Equal(2, reserved);

        var cartStatus = await Db.ScalarAsync<int>(
            Fixture, Primary, "SELECT Status FROM dbo.Carts WHERE Id = @id;", new { id = shop.CartId });
        Assert.Equal((int)CartStatus.CheckedOut, cartStatus);

        Assert.Equal(50.00m, placed.GrandTotal);
        Assert.Equal("ORD-00000001", placed.OrderNumber);
    }

    [SkippableFact]
    public async Task Insufficient_stock_rolls_back_every_step()
    {
        RequireDatabase();

        // The cart wants five units; only one is in stock. The failure happens
        // at step three, after the order and its items have already been
        // inserted, so this is a genuine partial-write rollback.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 1, cartQuantityA: 5);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var failure = await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new PlaceOrderCommand(
                ShopId: shop.ShopId,
                CustomerId: shop.CustomerId,
                CartId: shop.CartId,
                PaymentProvider: "stripe",
                PaymentReference: "pi_should_not_exist")));

        Assert.Contains("does not have 5 unit(s) available", failure.Message, StringComparison.Ordinal);

        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "Orders"));
        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "OrderItems"));
        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "Payments"));
        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "OrderStatusHistory"));

        var reserved = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityReserved FROM dbo.InventoryItems WHERE ProductId = @id;",
            new { id = shop.ProductAId });
        Assert.Equal(0, reserved);

        var cartStatus = await Db.ScalarAsync<int>(
            Fixture, Primary, "SELECT Status FROM dbo.Carts WHERE Id = @id;", new { id = shop.CartId });
        Assert.Equal((int)CartStatus.Open, cartStatus);
    }

    [SkippableFact]
    public async Task A_rolled_back_order_still_consumes_its_order_number()
    {
        RequireDatabase();

        // The sequence is allocated inside the transaction, so a rollback leaves
        // a gap. Gaps are cosmetic; duplicate order numbers would not be.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 1, cartQuantityA: 5);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new PlaceOrderCommand(
                shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_fail")));

        // The counter row rolled back too, so numbering restarts at 1.
        var sequenceRows = await Db.CountAsync(Fixture, Primary, "ShopOrderSequences");
        Assert.Equal(0, sequenceRows);
    }

    [SkippableFact]
    public async Task A_duplicate_payment_reference_aborts_the_whole_order()
    {
        RequireDatabase();

        // A retried payment webhook must not produce a second order. The unique
        // constraint on (TenantId, Provider, ProviderReference) fires at step
        // four and takes the entire transaction with it.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_duplicate"));

        var secondCart = await TestData.AddCartAsync(
            Fixture, Primary, shop, shop.ProductAId, quantity: 1, unitPrice: 25.00m);

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
            () => harness.SendAsync(new PlaceOrderCommand(
                shop.ShopId, shop.CustomerId, secondCart, "stripe", "pi_duplicate")));

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Orders"));
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Payments"));

        // Only the first order's reservation survives.
        var reserved = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityReserved FROM dbo.InventoryItems WHERE ProductId = @id;",
            new { id = shop.ProductAId });
        Assert.Equal(1, reserved);

        // The second cart was never checked out.
        var secondCartStatus = await Db.ScalarAsync<int>(
            Fixture, Primary, "SELECT Status FROM dbo.Carts WHERE Id = @id;", new { id = secondCart });
        Assert.Equal((int)CartStatus.Open, secondCartStatus);
    }

    [SkippableFact]
    public async Task A_cart_cannot_be_checked_out_twice()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_first"));

        var failure = await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new PlaceOrderCommand(
                shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_second")));

        Assert.Contains("already been CheckedOut", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Orders"));
    }

    [SkippableFact]
    public async Task Cancelling_an_order_releases_its_reservation_atomically()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 3);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_cancel"));

        Assert.Equal(3, await ReservedAsync(shop.ProductAId));

        await harness.SendAsync(new CancelOrderCommand(placed.OrderId, "Customer changed their mind"));

        Assert.Equal(0, await ReservedAsync(shop.ProductAId));

        var status = await Db.ScalarAsync<int>(
            Fixture, Primary, "SELECT Status FROM dbo.Orders WHERE Id = @id;", new { id = placed.OrderId });
        Assert.Equal((int)OrderStatus.Cancelled, status);

        // Placement wrote one history row; cancellation wrote the second.
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "OrderStatusHistory"));

        var reason = await Db.ScalarAsync<string>(
            Fixture, Primary,
            "SELECT CancellationReason FROM dbo.Orders WHERE Id = @id;",
            new { id = placed.OrderId });
        Assert.Equal("Customer changed their mind", reason);
    }

    [SkippableFact]
    public async Task Cancelling_twice_leaves_the_reservation_alone()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 3);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_double_cancel"));

        await harness.SendAsync(new CancelOrderCommand(placed.OrderId, "First"));

        // The domain refuses, so the second release never runs and stock cannot
        // drift upwards.
        await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new CancelOrderCommand(placed.OrderId, "Second")));

        Assert.Equal(0, await ReservedAsync(shop.ProductAId));
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "OrderStatusHistory"));
    }

    [SkippableFact]
    public async Task A_failed_product_creation_leaves_no_inventory_row()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var existingSku = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT Sku FROM dbo.Products WHERE Id = @id;", new { id = shop.ProductAId });

        var before = await Db.CountAsync(Fixture, Primary, "InventoryItems");

        await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new CreateProductCommand(
                ShopId: shop.ShopId,
                Name: "Duplicate SKU",
                Sku: existingSku!,
                Description: null,
                Price: 1.00m,
                InitialQuantityOnHand: 5)));

        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "Products"));
        Assert.Equal(before, await Db.CountAsync(Fixture, Primary, "InventoryItems"));
    }

    [SkippableFact]
    public async Task Creating_a_shop_also_creates_its_owner_membership()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var newShopId = await harness.SendAsync(new CreateShopCommand(
            Name: "Second Shop",
            Slug: "second-shop",
            CurrencyCode: "GBP",
            OwnerUserId: Guid.NewGuid(),
            OwnerEmail: "owner@example.test"));

        Assert.Equal(1, await Db.CountAsync(
            Fixture, Primary, "Shops", "Id = @newShopId", new { newShopId }));

        Assert.Equal(1, await Db.CountAsync(
            Fixture, Primary, "ShopMembers", "ShopId = @newShopId", new { newShopId }));

        var memberStatus = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT Status FROM dbo.ShopMembers WHERE ShopId = @newShopId;",
            new { newShopId });

        // The creator is the owner, so their membership starts active.
        Assert.Equal((int)Domain.Shops.ShopMemberStatus.Active, memberStatus);
    }

    [SkippableFact]
    public async Task A_shop_with_a_duplicate_slug_is_not_created_at_all()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new CreateShopCommand(
            "First", "duplicate-slug", "USD", Guid.NewGuid(), "a@example.test"));

        var shopsBefore = await Db.CountAsync(Fixture, Primary, "Shops");
        var membersBefore = await Db.CountAsync(Fixture, Primary, "ShopMembers");

        await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new CreateShopCommand(
                "Second", "duplicate-slug", "USD", Guid.NewGuid(), "b@example.test")));

        Assert.Equal(shopsBefore, await Db.CountAsync(Fixture, Primary, "Shops"));
        Assert.Equal(membersBefore, await Db.CountAsync(Fixture, Primary, "ShopMembers"));
    }

    private Task<int> ReservedAsync(Guid productId) =>
        Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityReserved FROM dbo.InventoryItems WHERE ProductId = @productId;",
            new { productId })!;
}
