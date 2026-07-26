using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Carts.Commands.AddToCart;
using OnlineShop.Application.Carts.Commands.RemoveCartItem;
using OnlineShop.Application.Carts.Commands.UpdateCartItem;
using OnlineShop.Application.Carts.Queries.GetCart;
using OnlineShop.Domain.Common;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// The basket commands and the read model behind the storefront cart page.
/// </summary>
public sealed class CartTests : IntegrationTest
{
    public CartTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task Adding_a_product_creates_a_basket_and_a_line()
    {
        RequireDatabase();

        // Seeded with no cart items of its own, so the command must create one.
        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 1);
        await Db.ExecuteAsync(Fixture, Primary, "DELETE FROM dbo.CartItems; DELETE FROM dbo.Carts;");

        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var cartId = await harness.SendAsync(
            new AddToCartCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 2));

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Carts"));
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "CartItems"));

        var cart = await harness.SendAsync(
            new GetCartQuery(shop.ShopId, shop.CustomerId, ReadConsistency.Strong));

        Assert.NotNull(cart);
        Assert.Equal(cartId, cart!.Id);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(25.00m, line.UnitPrice);
        Assert.Equal(50.00m, cart.Subtotal);
    }

    [SkippableFact]
    public async Task Adding_the_same_product_twice_merges_into_one_line()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 1);
        await Db.ExecuteAsync(Fixture, Primary, "DELETE FROM dbo.CartItems; DELETE FROM dbo.Carts;");

        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new AddToCartCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 2));
        await harness.SendAsync(new AddToCartCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 3));

        // One row, not two: the storefront shows one line per product, and a
        // second row would let the basket disagree with itself.
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "CartItems"));

        var cart = await harness.SendAsync(
            new GetCartQuery(shop.ShopId, shop.CustomerId, ReadConsistency.Strong));

        Assert.Equal(5, Assert.Single(cart!.Lines).Quantity);
    }

    [SkippableFact]
    public async Task A_basket_read_straight_after_a_write_needs_strong_consistency()
    {
        RequireDatabase();

        // The everyday read-after-write case in a storefront: add an item, then
        // immediately show the basket.
        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 1);
        await Db.ExecuteAsync(Fixture, Primary, "DELETE FROM dbo.CartItems; DELETE FROM dbo.Carts;");
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, shop.TenantId);

        await harness.SendAsync(new AddToCartCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 2));

        // The replica has not seen the basket at all yet.
        var eventual = await harness.SendAsync(new GetCartQuery(shop.ShopId, shop.CustomerId));
        Assert.Null(eventual);

        var strong = await harness.SendAsync(
            new GetCartQuery(shop.ShopId, shop.CustomerId, ReadConsistency.Strong));
        Assert.Equal(2, Assert.Single(strong!.Lines).Quantity);

        await Fixture.ReplicateAsync();

        var afterReplication = await harness.SendAsync(new GetCartQuery(shop.ShopId, shop.CustomerId));
        Assert.Equal(2, Assert.Single(afterReplication!.Lines).Quantity);
    }

    [SkippableFact]
    public async Task Setting_a_quantity_is_absolute_not_a_delta()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 2);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        // Repeating the same command must not keep changing the basket, which is
        // what a browser refresh or a double submit does.
        await harness.SendAsync(new UpdateCartItemCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 5));
        await harness.SendAsync(new UpdateCartItemCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 5));

        var cart = await harness.SendAsync(
            new GetCartQuery(shop.ShopId, shop.CustomerId, ReadConsistency.Strong));

        Assert.Equal(5, Assert.Single(cart!.Lines).Quantity);
    }

    [SkippableFact]
    public async Task Setting_a_quantity_to_zero_removes_the_line()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 2);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new UpdateCartItemCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 0));

        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "CartItems"));

        var cart = await harness.SendAsync(
            new GetCartQuery(shop.ShopId, shop.CustomerId, ReadConsistency.Strong));
        Assert.True(cart!.IsEmpty);
    }

    [SkippableFact]
    public async Task Removing_a_line_that_is_not_there_is_not_an_error()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 2);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        // The shopper's intent is already satisfied, so this is a no-op.
        await harness.SendAsync(new RemoveCartItemCommand(shop.ShopId, shop.CustomerId, shop.ProductBId));

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "CartItems"));
    }

    [SkippableFact]
    public async Task A_draft_product_cannot_be_added_to_a_basket()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await Db.ExecuteAsync(
            Fixture, Primary,
            "UPDATE dbo.Products SET Status = @Draft WHERE Id = @id;",
            new { Draft = (int)Domain.Catalog.ProductStatus.Draft, id = shop.ProductBId });

        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var failure = await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new AddToCartCommand(shop.ShopId, shop.CustomerId, shop.ProductBId, 1)));

        Assert.Contains("not available to buy", failure.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Another_tenants_product_cannot_be_added()
    {
        RequireDatabase();

        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);

        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(
                new AddToCartCommand(tenantA.ShopId, tenantA.CustomerId, tenantB.ProductAId, 1)));

        // Tenant B has its own legitimate line for that product, so the check
        // has to be scoped to tenant A rather than counting the product globally.
        Assert.Equal(0, await Db.CountAsync(
            Fixture, Primary,
            "CartItems",
            "TenantId = @tenantId AND ProductId = @productId",
            new { tenantId = tenantA.TenantId, productId = tenantB.ProductAId }));
    }

    [SkippableFact]
    public async Task The_basket_reports_lines_that_can_no_longer_be_supplied()
    {
        RequireDatabase();

        // Two units in the basket, one left in stock: the page must warn rather
        // than let checkout fail.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 1, cartQuantityA: 2);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var cart = await harness.SendAsync(
            new GetCartQuery(shop.ShopId, shop.CustomerId, ReadConsistency.Strong));

        Assert.True(cart!.HasUnavailableLines);
        var line = Assert.Single(cart.Lines);
        Assert.False(line.IsAvailable);
        Assert.Equal(1, line.QuantityAvailable);
    }

    [SkippableFact]
    public async Task The_basket_reports_a_price_that_has_moved_since_the_line_was_added()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 1, priceA: 25.00m);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await Db.ExecuteAsync(
            Fixture, Primary,
            "UPDATE dbo.Products SET Price = 30.00 WHERE Id = @id;",
            new { id = shop.ProductAId });

        var cart = await harness.SendAsync(
            new GetCartQuery(shop.ShopId, shop.CustomerId, ReadConsistency.Strong));

        var line = Assert.Single(cart!.Lines);
        Assert.True(line.PriceChanged);
        Assert.Equal(25.00m, line.UnitPrice);
        Assert.Equal(30.00m, line.CurrentPrice);
    }

    [SkippableFact]
    public async Task A_checked_out_basket_can_no_longer_be_changed()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new Application.Orders.Commands.PlaceOrder.PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_cart_closed"));

        // GetOpenCartAsync no longer finds it, so the command reports it missing.
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.SendAsync(
                new UpdateCartItemCommand(shop.ShopId, shop.CustomerId, shop.ProductAId, 4)));
    }
}
