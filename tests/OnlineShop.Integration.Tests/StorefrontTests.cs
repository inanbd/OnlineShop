using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Registration.Commands.RegisterMerchant;
using OnlineShop.Domain.Common;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// Storefront resolution and tenant provisioning &mdash; the two flows that run
/// without an established tenant.
/// </summary>
public sealed class StorefrontTests : IntegrationTest
{
    public StorefrontTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task A_slug_resolves_to_exactly_one_tenant_and_shop()
    {
        RequireDatabase();

        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        var slugA = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT Slug FROM dbo.Shops WHERE Id = @id;", new { id = tenantA.ShopId });

        await using var harness = TestHarness.Split(Fixture, Guid.Empty);
        var directory = harness.GetRequiredService<IShopDirectory>();

        var resolved = await directory.FindBySlugAsync(slugA!);

        Assert.NotNull(resolved);
        Assert.Equal(tenantA.TenantId, resolved!.TenantId);
        Assert.Equal(tenantA.ShopId, resolved.ShopId);
        Assert.NotEqual(tenantB.TenantId, resolved.TenantId);
    }

    [SkippableFact]
    public async Task An_unknown_or_blank_slug_resolves_to_nothing()
    {
        RequireDatabase();

        await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, Guid.Empty);
        var directory = harness.GetRequiredService<IShopDirectory>();

        Assert.Null(await directory.FindBySlugAsync("no-such-shop"));
        Assert.Null(await directory.FindBySlugAsync(""));
        Assert.Null(await directory.FindBySlugAsync("   "));
    }

    [SkippableFact]
    public async Task An_inactive_shop_is_not_reachable_from_the_storefront()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        var slug = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT Slug FROM dbo.Shops WHERE Id = @id;", new { id = shop.ShopId });

        await Db.ExecuteAsync(
            Fixture, Primary, "UPDATE dbo.Shops SET IsActive = 0 WHERE Id = @id;", new { id = shop.ShopId });
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, Guid.Empty);
        var directory = harness.GetRequiredService<IShopDirectory>();

        Assert.Null(await directory.FindBySlugAsync(slug!));
        Assert.Empty(await directory.ListActiveAsync());
    }

    [SkippableFact]
    public async Task The_directory_spans_tenants_but_shows_only_public_shop_identity()
    {
        RequireDatabase();

        await TestData.SeedAsync(Fixture, Primary);
        await TestData.SeedAsync(Fixture, Primary);
        await Fixture.ReplicateAsync();

        await using var harness = TestHarness.Split(Fixture, Guid.Empty);
        var directory = harness.GetRequiredService<IShopDirectory>();

        var shops = await directory.ListActiveAsync();

        // Crossing tenants is the point here; what matters is that the shape
        // carries nothing but public shop-front identity.
        Assert.Equal(2, shops.Count);
        Assert.All(shops, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.Slug));
            Assert.Equal(2, entry.ActiveProductCount);
        });
    }

    [SkippableFact]
    public async Task The_directory_is_served_from_the_replica()
    {
        RequireDatabase();

        await TestData.SeedAsync(Fixture, Primary);

        // Not replicated, so a directory read that reached the primary would
        // find the shop and this would fail.
        await using var harness = TestHarness.Split(Fixture, Guid.Empty);
        var directory = harness.GetRequiredService<IShopDirectory>();

        Assert.Empty(await directory.ListActiveAsync());
    }

    [SkippableFact]
    public async Task Registering_a_merchant_provisions_tenant_shop_and_owner_together()
    {
        RequireDatabase();

        // The one command with no ambient tenant: it creates one.
        await using var harness = TestHarness.SinglePrimary(Fixture, Guid.Empty);

        var result = await harness.SendAsync(new RegisterMerchantCommand(
            BusinessName: "Acme Supplies",
            TenantSlug: "acme",
            ShopName: "Acme Store",
            ShopSlug: "acme",
            CurrencyCode: "GBP",
            OwnerEmail: "owner@example.test",
            OwnerUserId: Guid.NewGuid()));

        Assert.NotEqual(Guid.Empty, result.TenantId);
        Assert.NotEqual(Guid.Empty, result.ShopId);

        Assert.Equal(1, await Db.CountAsync(
            Fixture, Primary, "Tenants", "Id = @id", new { id = result.TenantId }));
        Assert.Equal(1, await Db.CountAsync(
            Fixture, Primary, "Shops", "Id = @id", new { id = result.ShopId }));

        var memberStatus = await Db.ScalarAsync<int>(
            Fixture, Primary,
            "SELECT Status FROM dbo.ShopMembers WHERE ShopId = @id;",
            new { id = result.ShopId });

        // The creator is the owner, so their membership starts active.
        Assert.Equal((int)Domain.Shops.ShopMemberStatus.Active, memberStatus);

        var currency = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT CurrencyCode FROM dbo.Shops WHERE Id = @id;", new { id = result.ShopId });
        Assert.Equal("GBP", currency);
    }

    [SkippableFact]
    public async Task A_taken_tenant_slug_leaves_nothing_behind()
    {
        RequireDatabase();

        await using var harness = TestHarness.SinglePrimary(Fixture, Guid.Empty);

        await harness.SendAsync(new RegisterMerchantCommand(
            "First", "taken", "First Shop", "taken", "USD", "first@example.test", Guid.NewGuid()));

        var tenantsBefore = await Db.CountAsync(Fixture, Primary, "Tenants");
        var shopsBefore = await Db.CountAsync(Fixture, Primary, "Shops");

        var failure = await Assert.ThrowsAsync<DomainException>(
            () => harness.SendAsync(new RegisterMerchantCommand(
                "Second", "taken", "Second Shop", "taken", "USD", "second@example.test", Guid.NewGuid())));

        Assert.Contains("already taken", failure.Message, StringComparison.Ordinal);

        // The whole provisioning transaction rolled back.
        Assert.Equal(tenantsBefore, await Db.CountAsync(Fixture, Primary, "Tenants"));
        Assert.Equal(shopsBefore, await Db.CountAsync(Fixture, Primary, "Shops"));
    }

    [SkippableFact]
    public async Task Two_merchants_registering_get_separate_tenants()
    {
        RequireDatabase();

        await using var harness = TestHarness.SinglePrimary(Fixture, Guid.Empty);

        var first = await harness.SendAsync(new RegisterMerchantCommand(
            "Acme", "acme", "Acme Store", "acme", "USD", "a@example.test", Guid.NewGuid()));

        var second = await harness.SendAsync(new RegisterMerchantCommand(
            "Globex", "globex", "Globex Store", "globex", "EUR", "b@example.test", Guid.NewGuid()));

        Assert.NotEqual(first.TenantId, second.TenantId);
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "Tenants"));

        // Neither can see the other's catalog, which is the whole point.
        await using var acme = TestHarness.SinglePrimary(Fixture, first.TenantId);
        var acmeShops = await acme.GetRequiredService<IShopQueries>().ListAsync(first.TenantId);
        Assert.Equal("Acme Store", Assert.Single(acmeShops).Name);
    }
}
