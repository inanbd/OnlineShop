using Microsoft.Data.SqlClient;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Domain.Common;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Concurrent checkouts against real locks.
/// </summary>
/// <remarks>
/// These are the tests a fake connection cannot stand in for. Overselling and
/// duplicate order numbers are decided by the engine's locking behaviour, so
/// they can only be demonstrated by running genuinely concurrent transactions
/// against a real server.
/// </remarks>
public sealed class ConcurrencyTests : IntegrationTest
{
    public ConcurrencyTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task Only_one_of_six_checkouts_can_take_the_last_unit()
    {
        RequireDatabase();

        const int contenders = 6;

        // One unit in stock, six shoppers each trying to buy it.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 1, cartQuantityA: 1);

        var carts = new List<Guid> { shop.CartId };
        for (var i = 1; i < contenders; i++)
        {
            carts.Add(await TestData.AddCartAsync(
                Fixture, Primary, shop, shop.ProductAId, quantity: 1, unitPrice: 25.00m));
        }

        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var results = await RunConcurrentlyAsync(carts, cartId => harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, cartId, "stripe", $"pi_{cartId:N}")));

        var succeeded = results.Count(result => result.Succeeded);
        var soldOut = results.Count(result =>
            result.Error is DomainException && result.Error.Message.Contains("available", StringComparison.Ordinal));

        Assert.Equal(1, succeeded);
        Assert.Equal(contenders - 1, soldOut);

        // The books balance: one unit on hand, one reserved, one order.
        Assert.Equal(1, await ReservedAsync(shop.ProductAId));
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Orders"));
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Payments"));
    }

    [SkippableFact]
    public async Task Concurrent_checkouts_never_share_an_order_number()
    {
        RequireDatabase();

        const int shoppers = 8;

        // Plenty of stock, so every checkout should succeed and each must be
        // handed a distinct number by the MERGE on the sequence row.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, cartQuantityA: 1);

        var carts = new List<Guid> { shop.CartId };
        for (var i = 1; i < shoppers; i++)
        {
            carts.Add(await TestData.AddCartAsync(
                Fixture, Primary, shop, shop.ProductAId, quantity: 1, unitPrice: 25.00m));
        }

        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var results = await RunConcurrentlyAsync(carts, cartId => harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, cartId, "stripe", $"pi_{cartId:N}")));

        var failures = results.Where(result => !result.Succeeded).ToList();
        Assert.True(
            failures.Count == 0,
            $"Every checkout should have succeeded, but {failures.Count} failed: " +
            string.Join(" | ", failures.Select(f => $"{f.Error!.GetType().Name}: {f.Error.Message}")));

        var orderNumbers = results.Select(result => result.Value!.OrderNumber).ToList();

        Assert.Equal(shoppers, orderNumbers.Count);
        Assert.Equal(shoppers, orderNumbers.Distinct().Count());

        // The sequence is contiguous because none of these rolled back.
        Assert.Equal(
            Enumerable.Range(1, shoppers).Select(n => $"ORD-{n:D8}").OrderBy(n => n).ToList(),
            orderNumbers.OrderBy(n => n).ToList());

        Assert.Equal(shoppers, await Db.CountAsync(Fixture, Primary, "Orders"));
        Assert.Equal(shoppers, await ReservedAsync(shop.ProductAId));
    }

    [SkippableFact]
    public async Task Partial_availability_is_allocated_exactly_once_each()
    {
        RequireDatabase();

        const int shoppers = 8;
        const int stock = 3;

        // Three units, eight shoppers, two units each: exactly one shopper can
        // be served, because 2 does not divide into the remainder.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: stock, cartQuantityA: 2);

        var carts = new List<Guid> { shop.CartId };
        for (var i = 1; i < shoppers; i++)
        {
            carts.Add(await TestData.AddCartAsync(
                Fixture, Primary, shop, shop.ProductAId, quantity: 2, unitPrice: 25.00m));
        }

        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var results = await RunConcurrentlyAsync(carts, cartId => harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, cartId, "stripe", $"pi_{cartId:N}")));

        var succeeded = results.Count(result => result.Succeeded);

        Assert.Equal(1, succeeded);
        Assert.Equal(2, await ReservedAsync(shop.ProductAId));

        // Never more reserved than on hand, whatever the interleaving.
        var onHand = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityOnHand FROM dbo.InventoryItems WHERE ProductId = @id;",
            new { id = shop.ProductAId });
        Assert.True(await ReservedAsync(shop.ProductAId) <= onHand);
    }

    [SkippableFact]
    public async Task The_database_refuses_to_over_reserve_even_without_the_application()
    {
        RequireDatabase();

        // The application's conditional UPDATE is the first line of defence.
        // This is the second: a CHECK constraint that holds against an ad-hoc
        // statement run by hand.
        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 2);

        var failure = await Assert.ThrowsAsync<SqlException>(() => Db.ExecuteAsync(
            Fixture, Primary,
            "UPDATE dbo.InventoryItems SET QuantityReserved = 5 WHERE ProductId = @id;",
            new { id = shop.ProductAId }));

        Assert.Contains("CK_InventoryItems_NotOverReserved", failure.Message, StringComparison.Ordinal);
    }

    private Task<int> ReservedAsync(Guid productId) =>
        Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityReserved FROM dbo.InventoryItems WHERE ProductId = @productId;",
            new { productId })!;

    /// <summary>
    /// Releases every operation at the same moment, so they genuinely contend
    /// rather than running one after another.
    /// </summary>
    private static async Task<IReadOnlyList<Attempt<TResult>>> RunConcurrentlyAsync<TInput, TResult>(
        IEnumerable<TInput> inputs,
        Func<TInput, Task<TResult>> operation)
    {
        using var startingGun = new SemaphoreSlim(0);

        var attempts = inputs
            .Select(input => Task.Run(async () =>
            {
                await startingGun.WaitAsync();

                try
                {
                    return new Attempt<TResult>(await operation(input), null);
                }
                catch (Exception exception)
                {
                    return new Attempt<TResult>(default, exception);
                }
            }))
            .ToList();

        startingGun.Release(attempts.Count);

        return await Task.WhenAll(attempts);
    }

    private sealed record Attempt<TResult>(TResult? Value, Exception? Error)
    {
        public bool Succeeded => Error is null;
    }
}
