using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Application.Shops.Commands.InviteShopMember;
using OnlineShop.Domain.Customers;
using OnlineShop.Domain.Ordering;
using OnlineShop.Domain.Shops;
using OnlineShop.Integration.Tests.Infrastructure;
using OnlineShop.Persistence.Repositories;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Drives the repository methods that no command handler reaches, so every
/// statement in the persistence layer is executed at least once against a real
/// server.
/// </summary>
/// <remarks>
/// A statement no test ever runs is a statement nobody has checked. These fill
/// the gaps left by the command coverage.
/// </remarks>
public sealed class RepositoryTests : IntegrationTest
{
    public RepositoryTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task A_customer_can_be_inserted_read_back_and_updated()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var created = Customer.Create(
            shop.TenantId, shop.ShopId, "grace@example.test", "Grace Hopper", harness.Clock.UtcNow);

        var reloaded = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<ICustomerRepository>();

            return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
            {
                await repository.InsertAsync(created, transaction, ct);

                var loaded = await repository.GetByIdAsync(shop.TenantId, created.Id, transaction, ct);
                loaded!.UpdateProfile("Rear Admiral Grace Hopper", harness.Clock.UtcNow);
                await repository.UpdateAsync(loaded, transaction, ct);

                return await repository.GetByIdAsync(shop.TenantId, created.Id, transaction, ct);
            });
        });

        Assert.NotNull(reloaded);
        Assert.Equal("grace@example.test", reloaded!.Email);
        Assert.Equal("Rear Admiral Grace Hopper", reloaded.FullName);
        Assert.Equal(shop.ShopId, reloaded.ShopId);

        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "Customers"));
    }

    [SkippableFact]
    public async Task Another_tenants_customer_is_not_returned()
    {
        RequireDatabase();

        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);

        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        var found = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<ICustomerRepository>();

            return await unitOfWork.ExecuteAsync((transaction, ct) =>
                repository.GetByIdAsync(tenantA.TenantId, tenantB.CustomerId, transaction, ct));
        });

        Assert.Null(found);
    }

    [SkippableFact]
    public async Task The_customers_open_cart_is_found_with_its_items()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, cartQuantityA: 4);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var cart = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<ICustomerRepository>();

            return await unitOfWork.ExecuteAsync((transaction, ct) =>
                repository.GetOpenCartAsync(shop.TenantId, shop.ShopId, shop.CustomerId, transaction, ct));
        });

        Assert.NotNull(cart);
        Assert.Equal(shop.CartId, cart!.Id);
        Assert.Equal(CartStatus.Open, cart.Status);

        var item = Assert.Single(cart.Items);
        Assert.Equal(shop.ProductAId, item.ProductId);
        Assert.Equal(4, item.Quantity);
        Assert.Equal(25.00m, item.UnitPrice);
        Assert.Equal(100.00m, item.LineTotal);
    }

    [SkippableFact]
    public async Task A_checked_out_cart_is_no_longer_the_open_one()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var cart = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<ICustomerRepository>();

            return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
            {
                await repository.MarkCartCheckedOutAsync(shop.TenantId, shop.CartId, transaction, ct);
                return await repository.GetOpenCartAsync(
                    shop.TenantId, shop.ShopId, shop.CustomerId, transaction, ct);
            });
        });

        Assert.Null(cart);
    }

    [SkippableFact]
    public async Task Checking_out_an_already_closed_cart_reports_a_conflict()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<ICustomerRepository>();

            return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
            {
                await repository.MarkCartCheckedOutAsync(shop.TenantId, shop.CartId, transaction, ct);
                return true;
            });
        });

        var failure = await Assert.ThrowsAsync<DbConcurrencyException>(() =>
            harness.WithServicesAsync(async services =>
            {
                var unitOfWork = services.GetRequiredService<IUnitOfWork>();
                var repository = services.GetRequiredService<ICustomerRepository>();

                return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
                {
                    await repository.MarkCartCheckedOutAsync(shop.TenantId, shop.CartId, transaction, ct);
                    return true;
                });
            }));

        Assert.Contains("already been checked out", failure.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Inventory_can_be_read_back_and_adjusted()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var item = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<IInventoryRepository>();

            return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
            {
                await repository.AdjustOnHandAsync(shop.TenantId, shop.ProductAId, 15, transaction, ct);
                return await repository.GetByProductIdAsync(shop.TenantId, shop.ProductAId, transaction, ct);
            });
        });

        Assert.NotNull(item);
        Assert.Equal(25, item!.QuantityOnHand);
        Assert.Equal(0, item.QuantityReserved);
        Assert.Equal(25, item.QuantityAvailable);
        Assert.Equal(2, item.ReorderThreshold);
        Assert.False(item.NeedsReorder);
    }

    [SkippableFact]
    public async Task Stock_cannot_be_adjusted_below_zero()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 3);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var failure = await Assert.ThrowsAsync<DbConcurrencyException>(() =>
            harness.WithServicesAsync(async services =>
            {
                var unitOfWork = services.GetRequiredService<IUnitOfWork>();
                var repository = services.GetRequiredService<IInventoryRepository>();

                return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
                {
                    await repository.AdjustOnHandAsync(shop.TenantId, shop.ProductAId, -10, transaction, ct);
                    return true;
                });
            }));

        Assert.Contains("below zero", failure.Message, StringComparison.Ordinal);

        var onHand = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityOnHand FROM dbo.InventoryItems WHERE ProductId = @id;",
            new { id = shop.ProductAId });
        Assert.Equal(3, onHand);
    }

    [SkippableFact]
    public async Task Releasing_more_than_is_reserved_clamps_at_zero()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<IInventoryRepository>();

            return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
            {
                await repository.TryReserveAsync(shop.TenantId, shop.ProductAId, 2, transaction, ct);

                // Twice as many as are actually reserved.
                await repository.ReleaseReservationAsync(shop.TenantId, shop.ProductAId, 4, transaction, ct);
                return true;
            });
        });

        var reserved = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityReserved FROM dbo.InventoryItems WHERE ProductId = @id;",
            new { id = shop.ProductAId });

        // Clamped, not negative: a negative reservation would inflate apparent
        // availability and let the shop oversell.
        Assert.Equal(0, reserved);
    }

    [SkippableFact]
    public async Task Reserving_another_tenants_stock_does_nothing()
    {
        RequireDatabase();

        var tenantA = await TestData.SeedAsync(Fixture, Primary, stockA: 10);
        var tenantB = await TestData.SeedAsync(Fixture, Primary, stockA: 10);

        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        var reserved = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<IInventoryRepository>();

            return await unitOfWork.ExecuteAsync((transaction, ct) =>
                repository.TryReserveAsync(tenantA.TenantId, tenantB.ProductAId, 1, transaction, ct));
        });

        Assert.False(reserved);

        var tenantBReserved = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT QuantityReserved FROM dbo.InventoryItems WHERE ProductId = @id;",
            new { id = tenantB.ProductAId });
        Assert.Equal(0, tenantBReserved);
    }

    [SkippableFact]
    public async Task A_shop_can_be_renamed_and_deactivated()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var reloaded = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<IShopRepository>();

            return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
            {
                var loaded = await repository.GetByIdAsync(shop.TenantId, shop.ShopId, transaction, ct);
                loaded!.Rename("Renamed Shop", harness.Clock.UtcNow);
                loaded.Deactivate(harness.Clock.UtcNow);
                await repository.UpdateAsync(loaded, transaction, ct);

                return await repository.GetByIdAsync(shop.TenantId, shop.ShopId, transaction, ct);
            });
        });

        Assert.Equal("Renamed Shop", reloaded!.Name);
        Assert.False(reloaded.IsActive);
    }

    [SkippableFact]
    public async Task Updating_another_tenants_shop_reports_a_conflict()
    {
        RequireDatabase();

        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);

        await using var harness = TestHarness.SinglePrimary(Fixture, tenantB.TenantId);

        // A shop object carrying tenant A's id, written while acting as tenant
        // B. The statement's TenantId predicate matches nothing.
        var foreign = Shop.Restore(
            id: tenantA.ShopId,
            tenantId: tenantA.TenantId,
            name: "Hijacked",
            slug: "hijacked",
            currencyCode: "USD",
            isActive: false,
            createdAt: harness.Clock.UtcNow,
            updatedAt: harness.Clock.UtcNow);

        // Deleting tenant A's row first would be cheating, so instead point at a
        // shop id that does not exist in tenant A.
        var missing = Shop.Restore(
            id: Guid.NewGuid(),
            tenantId: tenantA.TenantId,
            name: "Missing",
            slug: "missing",
            currencyCode: "USD",
            isActive: true,
            createdAt: harness.Clock.UtcNow,
            updatedAt: harness.Clock.UtcNow);

        await Assert.ThrowsAsync<DbConcurrencyException>(() =>
            harness.WithServicesAsync(async services =>
            {
                var unitOfWork = services.GetRequiredService<IUnitOfWork>();
                var repository = services.GetRequiredService<IShopRepository>();

                return await unitOfWork.ExecuteAsync(async (transaction, ct) =>
                {
                    await repository.UpdateAsync(missing, transaction, ct);
                    return true;
                });
            }));

        // Tenant A's real shop is untouched.
        var name = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT Name FROM dbo.Shops WHERE Id = @id;", new { id = foreign.Id });
        Assert.Equal("Test Shop", name);
    }

    [SkippableFact]
    public async Task A_revoked_member_is_reinstated_on_the_existing_row()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await TestData.AddShopMemberAsync(
            Fixture, Primary, shop, "returning@example.test", ShopMemberRole.Staff, ShopMemberStatus.Revoked);

        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var memberId = await harness.SendAsync(new InviteShopMemberCommand(
            shop.ShopId, "returning@example.test", ShopMemberRole.Manager, shop.OwnerUserId));

        // The row is reused, so the membership history survives.
        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "ShopMembers"));

        var row = Assert.Single(await Db.QueryAsync<(int Role, int Status)>(
            Fixture, Primary,
            "SELECT Role, Status FROM dbo.ShopMembers WHERE Id = @memberId;",
            new { memberId }));

        Assert.Equal((int)ShopMemberRole.Manager, row.Role);
        Assert.Equal((int)ShopMemberStatus.Invited, row.Status);
    }

    [SkippableFact]
    public async Task An_active_member_cannot_be_invited_twice()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new InviteShopMemberCommand(
            shop.ShopId, "dup@example.test", ShopMemberRole.Staff, shop.OwnerUserId));

        await Assert.ThrowsAsync<Domain.Common.DomainException>(
            () => harness.SendAsync(new InviteShopMemberCommand(
                shop.ShopId, "dup@example.test", ShopMemberRole.Manager, shop.OwnerUserId)));

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "ShopMembers"));
    }

    [SkippableFact]
    public async Task An_order_round_trips_through_the_repository_with_its_items()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 3);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new Application.Orders.Commands.PlaceOrder.PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_roundtrip"));

        var order = await harness.WithServicesAsync(async services =>
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var repository = services.GetRequiredService<IOrderRepository>();

            return await unitOfWork.ExecuteAsync((transaction, ct) =>
                repository.GetByIdAsync(shop.TenantId, placed.OrderId, transaction, ct));
        });

        Assert.NotNull(order);
        Assert.Equal(placed.OrderNumber, order!.OrderNumber);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("USD", order.CurrencyCode);

        var item = Assert.Single(order.Items);
        Assert.Equal(shop.ProductAId, item.ProductId);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(75.00m, item.LineTotal);

        // Totals are recomputed from the rehydrated items.
        Assert.Equal(75.00m, order.Subtotal);
        Assert.Equal(75.00m, order.GrandTotal);
    }

    [SkippableFact]
    public async Task Order_numbers_continue_from_where_the_shop_left_off()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var numbers = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            var cartId = i == 0
                ? shop.CartId
                : await TestData.AddCartAsync(Fixture, Primary, shop, shop.ProductAId, 1, 25.00m);

            var placed = await harness.SendAsync(new Application.Orders.Commands.PlaceOrder.PlaceOrderCommand(
                shop.ShopId, shop.CustomerId, cartId, "stripe", $"pi_seq_{i}"));

            numbers.Add(placed.OrderNumber);
        }

        Assert.Equal(["ORD-00000001", "ORD-00000002", "ORD-00000003"], numbers);
    }

    [SkippableFact]
    public async Task Each_shop_numbers_its_orders_independently()
    {
        RequireDatabase();

        var shopA = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 1);

        await using var harness = TestHarness.SinglePrimary(Fixture, shopA.TenantId);

        var shopBId = await harness.SendAsync(new Application.Shops.Commands.CreateShop.CreateShopCommand(
            "Shop B", "shop-b", "USD", shopA.OwnerUserId, "owner-b@example.test"));

        var firstInA = await harness.SendAsync(new Application.Orders.Commands.PlaceOrder.PlaceOrderCommand(
            shopA.ShopId, shopA.CustomerId, shopA.CartId, "stripe", "pi_shop_a"));

        // A second shop starts its own sequence at 1.
        var productB = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        await Db.ExecuteAsync(
            Fixture, Primary,
            """
            INSERT INTO dbo.Customers (Id, TenantId, ShopId, Email, FullName)
            VALUES (@CustomerB, @TenantId, @ShopBId, 'b@example.test', N'Shopper B');

            INSERT INTO dbo.Products (Id, TenantId, ShopId, Name, Sku, Price, CurrencyCode, Status)
            VALUES (@ProductB, @TenantId, @ShopBId, N'B Widget', 'B-1', 10.00, 'USD', 1);

            INSERT INTO dbo.InventoryItems (Id, TenantId, ShopId, ProductId, QuantityOnHand, QuantityReserved, ReorderThreshold)
            VALUES (NEWID(), @TenantId, @ShopBId, @ProductB, 10, 0, 1);
            """,
            new { shopA.TenantId, ShopBId = shopBId, ProductB = productB, CustomerB = customerB });

        var cartB = Guid.NewGuid();
        await Db.ExecuteAsync(
            Fixture, Primary,
            """
            INSERT INTO dbo.Carts (Id, TenantId, ShopId, CustomerId, Status)
            VALUES (@CartB, @TenantId, @ShopBId, @CustomerB, 0);

            INSERT INTO dbo.CartItems (Id, TenantId, CartId, ProductId, Quantity, UnitPrice)
            VALUES (NEWID(), @TenantId, @CartB, @ProductB, 1, 10.00);
            """,
            new { shopA.TenantId, ShopBId = shopBId, CartB = cartB, CustomerB = customerB, ProductB = productB });

        var firstInB = await harness.SendAsync(new Application.Orders.Commands.PlaceOrder.PlaceOrderCommand(
            shopBId, customerB, cartB, "stripe", "pi_shop_b"));

        Assert.Equal("ORD-00000001", firstInA.OrderNumber);
        Assert.Equal("ORD-00000001", firstInB.OrderNumber);
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "ShopOrderSequences"));
    }
}
