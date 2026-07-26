using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Customers;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Dashboard.Queries.GetShopDashboard;
using OnlineShop.Application.Orders.Commands.CancelOrder;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Application.Orders.Queries.GetOrderDetails;
using OnlineShop.Application.Orders.Queries.GetOrders;
using OnlineShop.Application.Products.Queries.GetProducts;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Ordering;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Executes every read-model statement against real tables.
/// </summary>
/// <remarks>
/// SQL in a string constant compiles no matter what it says. A wrong column
/// name, a type SQL Server will not convert, or a projection Dapper cannot map
/// onto its DTO only shows up when the statement actually runs — which is what
/// these tests do.
/// </remarks>
public sealed class QueryTests : IntegrationTest
{
    public QueryTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    // ---------- Catalog ----------

    [SkippableFact]
    public async Task Product_details_project_every_column_including_stock()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 12);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await Db.ExecuteAsync(
            Fixture, Primary,
            "UPDATE dbo.InventoryItems SET QuantityReserved = 4 WHERE ProductId = @id;",
            new { id = shop.ProductAId });

        var queries = harness.GetRequiredService<IProductQueries>();
        var product = await queries.GetByIdAsync(shop.TenantId, shop.ProductAId);

        Assert.NotNull(product);
        Assert.Equal(shop.ProductAId, product!.Id);
        Assert.Equal(shop.ShopId, product.ShopId);
        Assert.Equal("Test Shop", product.ShopName);
        Assert.Equal("Widget A", product.Name);
        Assert.Equal(25.00m, product.Price);
        Assert.Equal("USD", product.CurrencyCode);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.Equal(12, product.QuantityOnHand);
        Assert.Equal(4, product.QuantityReserved);
        Assert.Equal(8, product.QuantityAvailable);
        Assert.NotEqual(default, product.CreatedAt);
    }

    [SkippableFact]
    public async Task A_soft_deleted_product_disappears_from_details_and_listings()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new Application.Products.Commands.DeleteProduct.DeleteProductCommand(shop.ProductAId));

        var queries = harness.GetRequiredService<IProductQueries>();

        Assert.Null(await queries.GetByIdAsync(shop.TenantId, shop.ProductAId));

        var visible = await harness.SendAsync(new GetProductsQuery(new ProductFilter()));
        Assert.Equal(1, visible.TotalCount);

        var includingDeleted = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { IncludeDeleted = true }));
        Assert.Equal(2, includingDeleted.TotalCount);
    }

    [SkippableFact]
    public async Task Product_listing_applies_every_filter()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, priceA: 25.00m, priceB: 40.00m);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await AddProductAsync(shop, "Draft Widget", "DRAFT-1", 10.00m, ProductStatus.Draft);

        var byShop = await harness.SendAsync(new GetProductsQuery(new ProductFilter { ShopId = shop.ShopId }));
        Assert.Equal(3, byShop.TotalCount);

        var byOtherShop = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { ShopId = Guid.NewGuid() }));
        Assert.Equal(0, byOtherShop.TotalCount);

        var active = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { Status = ProductStatus.Active }));
        Assert.Equal(2, active.TotalCount);

        var draft = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { Status = ProductStatus.Draft }));
        Assert.Equal(1, draft.TotalCount);

        var priced = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { MinPrice = 20.00m, MaxPrice = 30.00m }));
        Assert.Equal(1, priced.TotalCount);
        Assert.Equal("Widget A", priced.Items[0].Name);

        var searched = await harness.SendAsync(
            new GetProductsQuery(new ProductFilter { SearchTerm = "Widget B" }));
        Assert.Equal(1, searched.TotalCount);
        Assert.Equal("Widget B", searched.Items[0].Name);
    }

    [SkippableFact]
    public async Task Search_treats_an_underscore_as_a_literal_not_a_wildcard()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await AddProductAsync(shop, "A_B", "LIT-1", 1.00m, ProductStatus.Active);
        await AddProductAsync(shop, "AXB", "LIT-2", 1.00m, ProductStatus.Active);

        // Unescaped, '_' matches any single character and would return both.
        var page = await harness.SendAsync(new GetProductsQuery(new ProductFilter { SearchTerm = "A_B" }));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("A_B", page.Items[0].Name);
    }

    [SkippableFact]
    public async Task Search_treats_a_percent_sign_as_a_literal_not_a_wildcard()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await AddProductAsync(shop, "100% Cotton", "PCT-1", 1.00m, ProductStatus.Active);
        await AddProductAsync(shop, "100 Percent Wool", "PCT-2", 1.00m, ProductStatus.Active);

        // Unescaped, '%' matches anything and would return both.
        var page = await harness.SendAsync(new GetProductsQuery(new ProductFilter { SearchTerm = "100%" }));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("100% Cotton", page.Items[0].Name);
    }

    [SkippableFact]
    public async Task Product_listing_pages_without_dropping_or_repeating_rows()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        for (var i = 0; i < 8; i++)
        {
            await AddProductAsync(shop, $"Paged {i:D2}", $"PAGE-{i:D2}", 1.00m + i, ProductStatus.Active);
        }

        var seen = new List<Guid>();

        for (var page = 1; page <= 4; page++)
        {
            var result = await harness.SendAsync(
                new GetProductsQuery(new ProductFilter { Page = page, PageSize = 3 }));

            Assert.Equal(10, result.TotalCount);
            Assert.Equal(4, result.TotalPages);
            seen.AddRange(result.Items.Select(item => item.Id));
        }

        Assert.Equal(10, seen.Count);
        Assert.Equal(10, seen.Distinct().Count());
    }

    // ---------- Orders ----------

    [SkippableFact]
    public async Task Order_details_return_all_four_result_sets()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 3);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_details"));

        var details = await harness.SendAsync(new GetOrderDetailsQuery(placed.OrderId));

        Assert.Equal(placed.OrderId, details.Id);
        Assert.Equal("Test Shop", details.ShopName);
        Assert.Equal("Ada Lovelace", details.CustomerName);
        Assert.Equal(OrderStatus.Pending, details.Status);
        Assert.Equal(75.00m, details.GrandTotal);
        Assert.Equal("USD", details.CurrencyCode);
        Assert.Null(details.CancelledAt);
        Assert.Null(details.CancellationReason);

        var item = Assert.Single(details.Items);
        Assert.Equal(shop.ProductAId, item.ProductId);
        Assert.Equal("Widget A", item.ProductName);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(25.00m, item.UnitPrice);
        Assert.Equal(75.00m, item.LineTotal);

        var payment = Assert.Single(details.Payments);
        Assert.Equal("stripe", payment.Provider);
        Assert.Equal("pi_details", payment.ProviderReference);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(75.00m, payment.Amount);

        var history = Assert.Single(details.StatusHistory);
        Assert.Null(history.FromStatus);
        Assert.Equal(OrderStatus.Pending, history.ToStatus);
        Assert.Equal("Order placed.", history.Reason);
    }

    [SkippableFact]
    public async Task A_cancelled_order_projects_its_cancellation_and_both_history_rows()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 10, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var placed = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_cancelled"));

        harness.Clock.Advance(TimeSpan.FromHours(2));
        await harness.SendAsync(new CancelOrderCommand(placed.OrderId, "Out of stock at the warehouse"));

        var details = await harness.SendAsync(new GetOrderDetailsQuery(placed.OrderId));

        Assert.Equal(OrderStatus.Cancelled, details.Status);
        Assert.NotNull(details.CancelledAt);
        Assert.Equal("Out of stock at the warehouse", details.CancellationReason);
        Assert.Equal(2, details.StatusHistory.Count);

        // Ordered by OccurredAt, so the transition reads in sequence.
        Assert.Null(details.StatusHistory[0].FromStatus);
        Assert.Equal(OrderStatus.Pending, details.StatusHistory[1].FromStatus);
        Assert.Equal(OrderStatus.Cancelled, details.StatusHistory[1].ToStatus);
    }

    [SkippableFact]
    public async Task Order_listing_applies_every_filter()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var first = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_1"));

        harness.Clock.Advance(TimeSpan.FromDays(1));

        var secondCart = await TestData.AddCartAsync(Fixture, Primary, shop, shop.ProductAId, 2, 25.00m);
        var second = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, secondCart, "stripe", "pi_2"));

        await harness.SendAsync(new CancelOrderCommand(second.OrderId, "Changed mind"));

        var all = await harness.SendAsync(new GetOrdersQuery(new OrderFilter()));
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(second.OrderId, all.Items[0].Id); // newest first

        var pending = await harness.SendAsync(
            new GetOrdersQuery(new OrderFilter { Status = OrderStatus.Pending }));
        Assert.Equal(1, pending.TotalCount);
        Assert.Equal(first.OrderId, pending.Items[0].Id);

        var cancelled = await harness.SendAsync(
            new GetOrdersQuery(new OrderFilter { Status = OrderStatus.Cancelled }));
        Assert.Equal(1, cancelled.TotalCount);

        var byShop = await harness.SendAsync(new GetOrdersQuery(new OrderFilter { ShopId = shop.ShopId }));
        Assert.Equal(2, byShop.TotalCount);

        var byCustomer = await harness.SendAsync(
            new GetOrdersQuery(new OrderFilter { CustomerId = shop.CustomerId }));
        Assert.Equal(2, byCustomer.TotalCount);

        var byNumber = await harness.SendAsync(
            new GetOrdersQuery(new OrderFilter { OrderNumber = first.OrderNumber }));
        Assert.Equal(1, byNumber.TotalCount);

        var byNumberPrefix = await harness.SendAsync(new GetOrdersQuery(new OrderFilter { OrderNumber = "ORD-" }));
        Assert.Equal(2, byNumberPrefix.TotalCount);

        var listItem = all.Items.Single(order => order.Id == second.OrderId);
        Assert.Equal(1, listItem.ItemCount);
        Assert.Equal(50.00m, listItem.GrandTotal);
        Assert.Contains("@example.test", listItem.CustomerEmail, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Order_listing_filters_by_placement_window()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var dayOne = harness.Clock.UtcNow;
        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_day1"));

        harness.Clock.Advance(TimeSpan.FromDays(5));
        var daySix = harness.Clock.UtcNow;

        var laterCart = await TestData.AddCartAsync(Fixture, Primary, shop, shop.ProductAId, 1, 25.00m);
        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, laterCart, "stripe", "pi_day6"));

        var early = await harness.SendAsync(new GetOrdersQuery(new OrderFilter
        {
            PlacedFromUtc = dayOne.AddMinutes(-1),
            PlacedToUtc = dayOne.AddMinutes(1),
        }));
        Assert.Equal(1, early.TotalCount);

        var late = await harness.SendAsync(new GetOrdersQuery(new OrderFilter
        {
            PlacedFromUtc = daySix.AddMinutes(-1),
        }));
        Assert.Equal(1, late.TotalCount);
    }

    // ---------- Customers ----------

    [SkippableFact]
    public async Task Customer_listing_aggregates_orders_and_excludes_cancellations()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, cartQuantityA: 2);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_c1"));

        var secondCart = await TestData.AddCartAsync(Fixture, Primary, shop, shop.ProductAId, 1, 25.00m);
        var cancelled = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, secondCart, "stripe", "pi_c2"));
        await harness.SendAsync(new CancelOrderCommand(cancelled.OrderId, "Cancelled"));

        var queries = harness.GetRequiredService<ICustomerQueries>();
        var customers = await queries.GetAsync(shop.TenantId, new CustomerFilter());

        var customer = Assert.Single(customers);
        Assert.Equal(shop.CustomerId, customer.Id);
        Assert.Equal("Ada Lovelace", customer.FullName);

        // 2 x 25.00 from the surviving order; the cancelled one is excluded.
        Assert.Equal(1, customer.OrderCount);
        Assert.Equal(50.00m, customer.LifetimeValue);
        Assert.NotNull(customer.LastOrderAt);

        Assert.Equal(1, await queries.CountAsync(shop.TenantId, new CustomerFilter()));
    }

    [SkippableFact]
    public async Task A_customer_with_no_orders_reports_zeroes_rather_than_nulls()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var queries = harness.GetRequiredService<ICustomerQueries>();
        var customer = Assert.Single(await queries.GetAsync(shop.TenantId, new CustomerFilter()));

        Assert.Equal(0, customer.OrderCount);
        Assert.Equal(0m, customer.LifetimeValue);
        Assert.Null(customer.LastOrderAt);
    }

    [SkippableFact]
    public async Task Customer_listing_filters_by_search_and_shop()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var queries = harness.GetRequiredService<ICustomerQueries>();

        Assert.Single(await queries.GetAsync(shop.TenantId, new CustomerFilter { SearchTerm = "Ada" }));
        Assert.Empty(await queries.GetAsync(shop.TenantId, new CustomerFilter { SearchTerm = "Grace" }));
        Assert.Single(await queries.GetAsync(shop.TenantId, new CustomerFilter { ShopId = shop.ShopId }));
        Assert.Empty(await queries.GetAsync(shop.TenantId, new CustomerFilter { ShopId = Guid.NewGuid() }));
    }

    // ---------- Dashboard ----------

    [SkippableFact]
    public async Task The_dashboard_aggregates_every_panel()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, stockB: 3, cartQuantityA: 2);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        // The dashboard measures a window ending at the wall clock, so orders
        // have to be placed at a time that falls inside it.
        harness.Clock.Set(DateTime.UtcNow.AddHours(-1));

        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_d1"));

        var cartB = await TestData.AddCartAsync(Fixture, Primary, shop, shop.ProductBId, 1, 40.00m);
        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, cartB, "stripe", "pi_d2"));

        var cancelledCart = await TestData.AddCartAsync(Fixture, Primary, shop, shop.ProductAId, 4, 25.00m);
        var cancelled = await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, cancelledCart, "stripe", "pi_d3"));
        await harness.SendAsync(new CancelOrderCommand(cancelled.OrderId, "Cancelled"));

        var dashboard = await harness.SendAsync(new GetShopDashboardQuery(shop.ShopId));

        Assert.Equal(shop.ShopId, dashboard.ShopId);
        Assert.Equal("Test Shop", dashboard.ShopName);
        Assert.Equal("USD", dashboard.CurrencyCode);
        Assert.Equal(30, dashboard.WindowDays);

        // 50.00 + 40.00; the cancelled 100.00 order is excluded.
        Assert.Equal(90.00m, dashboard.RevenueInWindow);
        Assert.Equal(2, dashboard.OrdersInWindow);
        Assert.Equal(45.00m, dashboard.AverageOrderValue);

        // Status counts include cancellations.
        Assert.Equal(2, dashboard.OrdersByStatus.Single(s => s.Status == OrderStatus.Pending).Count);
        Assert.Equal(1, dashboard.OrdersByStatus.Single(s => s.Status == OrderStatus.Cancelled).Count);

        Assert.Equal(2, dashboard.TopProducts.Count);
        var best = dashboard.TopProducts[0];
        Assert.Equal(shop.ProductAId, best.ProductId);
        Assert.Equal("Widget A", best.Name);
        Assert.Equal(2, best.UnitsSold);
        Assert.Equal(50.00m, best.Revenue);

        var day = Assert.Single(dashboard.DailyRevenue);
        Assert.Equal(90.00m, day.Revenue);
        Assert.Equal(2, day.OrderCount);

        Assert.Equal(2, dashboard.ActiveProductCount);
        Assert.Equal(1, dashboard.NewCustomersInWindow);

        // Widget B: 3 on hand, 1 reserved, threshold 2 -> available 2 <= 2.
        Assert.Equal(1, dashboard.LowStockProductCount);
    }

    [SkippableFact]
    public async Task An_empty_shop_dashboard_returns_zeroes_not_nulls()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, stockB: 50);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var dashboard = await harness.SendAsync(new GetShopDashboardQuery(shop.ShopId));

        Assert.Equal(0m, dashboard.RevenueInWindow);
        Assert.Equal(0, dashboard.OrdersInWindow);
        Assert.Equal(0m, dashboard.AverageOrderValue);
        Assert.Empty(dashboard.OrdersByStatus);
        Assert.Empty(dashboard.TopProducts);
        Assert.Empty(dashboard.DailyRevenue);
        Assert.Equal(0, dashboard.LowStockProductCount);
    }

    [SkippableFact]
    public async Task The_dashboard_window_excludes_older_orders()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary, stockA: 50, cartQuantityA: 1);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        // Placed 60 days ago, so a 30-day window must not see it.
        harness.Clock.Set(DateTime.UtcNow.AddDays(-60));
        await harness.SendAsync(new PlaceOrderCommand(
            shop.ShopId, shop.CustomerId, shop.CartId, "stripe", "pi_old"));

        var thirtyDays = await harness.SendAsync(new GetShopDashboardQuery(shop.ShopId, WindowDays: 30));
        Assert.Equal(0, thirtyDays.OrdersInWindow);

        var ninetyDays = await harness.SendAsync(new GetShopDashboardQuery(shop.ShopId, WindowDays: 90));
        Assert.Equal(1, ninetyDays.OrdersInWindow);
        Assert.Equal(25.00m, ninetyDays.RevenueInWindow);
    }

    [SkippableFact]
    public async Task Strong_consistency_is_accepted_when_read_and_write_share_a_database()
    {
        RequireDatabase();

        // Nothing special happens in a single-database environment, but the
        // routing must still work rather than throwing.
        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var queries = harness.GetRequiredService<IProductQueries>();

        var eventual = await queries.GetByIdAsync(shop.TenantId, shop.ProductAId, ReadConsistency.Eventual);
        var strong = await queries.GetByIdAsync(shop.TenantId, shop.ProductAId, ReadConsistency.Strong);

        Assert.NotNull(eventual);
        Assert.NotNull(strong);
        Assert.Equal(eventual!.Id, strong!.Id);
    }

    private Task AddProductAsync(SeededShop shop, string name, string sku, decimal price, ProductStatus status) =>
        Db.ExecuteAsync(
            Fixture, Primary,
            """
            INSERT INTO dbo.Products (Id, TenantId, ShopId, Name, Sku, Price, CurrencyCode, Status)
            VALUES (@Id, @TenantId, @ShopId, @Name, @Sku, @Price, 'USD', @Status);
            """,
            new
            {
                Id = Guid.NewGuid(),
                shop.TenantId,
                shop.ShopId,
                Name = name,
                Sku = sku,
                Price = price,
                Status = (int)status,
            });
}
