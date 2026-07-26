using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Abstractions.Identity;
using OnlineShop.Integration.Tests.Infrastructure;

namespace OnlineShop.Integration.Tests;

/// <summary>
/// The Dapper-backed ASP.NET Core Identity stores, against a real database.
/// </summary>
/// <remarks>
/// Identity ships an Entity Framework store, which rule 1 forbids, so these
/// are hand-written. That makes them exactly the kind of code that has to be
/// tested against a real server: the interfaces are broad and a mistake in any
/// one of them shows up as a login that silently fails.
/// </remarks>
public sealed class IdentityStoreTests : IntegrationTest
{
    public IdentityStoreTests(SqlServerFixture fixture)
        : base(fixture)
    {
    }

    [SkippableFact]
    public async Task A_user_can_be_created_and_found_by_id_name_and_email()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var user = NewUser(shop.TenantId, "ada@example.test");

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();
            var created = await store.CreateAsync(user, CancellationToken.None);
            Assert.True(created.Succeeded);
            return true;
        });

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();
            var emailStore = (IUserEmailStore<AppUser>)store;

            var byId = await store.FindByIdAsync(user.Id.ToString(), CancellationToken.None);
            var byName = await store.FindByNameAsync("ADA@EXAMPLE.TEST", CancellationToken.None);
            var byEmail = await emailStore.FindByEmailAsync("ADA@EXAMPLE.TEST", CancellationToken.None);

            Assert.NotNull(byId);
            Assert.NotNull(byName);
            Assert.NotNull(byEmail);

            Assert.Equal(shop.TenantId, byId!.TenantId);
            Assert.Equal("ada@example.test", byEmail!.Email);
            Assert.Equal("hashed-password", byId.PasswordHash);
            return true;
        });
    }

    [SkippableFact]
    public async Task An_unknown_user_is_not_found_rather_than_throwing()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();

            Assert.Null(await store.FindByIdAsync(Guid.NewGuid().ToString(), CancellationToken.None));
            Assert.Null(await store.FindByNameAsync("NOBODY@EXAMPLE.TEST", CancellationToken.None));

            // A malformed id must not throw either; it simply matches nothing.
            Assert.Null(await store.FindByIdAsync("not-a-guid", CancellationToken.None));
            return true;
        });
    }

    [SkippableFact]
    public async Task A_duplicate_email_is_reported_as_a_failure_not_an_exception()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();

            var first = await store.CreateAsync(NewUser(shop.TenantId, "dup@example.test"), CancellationToken.None);
            Assert.True(first.Succeeded);

            // The unique index is the authority; the store turns the violation
            // into something Identity can show the user.
            var second = await store.CreateAsync(NewUser(shop.TenantId, "dup@example.test"), CancellationToken.None);
            Assert.False(second.Succeeded);
            Assert.Contains(second.Errors, e => e.Code == "DuplicateUserName");
            return true;
        });

        Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "Users"));
    }

    [SkippableFact]
    public async Task Sign_in_identifiers_are_unique_across_tenants()
    {
        RequireDatabase();

        // Sign-in happens before any tenant is known, so one address cannot
        // belong to two tenants.
        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);

        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();

            Assert.True((await store.CreateAsync(
                NewUser(tenantA.TenantId, "shared@example.test"), CancellationToken.None)).Succeeded);

            Assert.False((await store.CreateAsync(
                NewUser(tenantB.TenantId, "shared@example.test"), CancellationToken.None)).Succeeded);
            return true;
        });
    }

    [SkippableFact]
    public async Task Roles_can_be_granted_read_back_and_revoked()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var user = NewUser(shop.TenantId, "roles@example.test");

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();
            var roleStore = (IUserRoleStore<AppUser>)store;

            await store.CreateAsync(user, CancellationToken.None);

            // UserManager normalises before calling the store, so this is the
            // shape the store actually receives.
            await roleStore.AddToRoleAsync(user, "MERCHANT", CancellationToken.None);

            Assert.True(await roleStore.IsInRoleAsync(user, "MERCHANT", CancellationToken.None));
            Assert.False(await roleStore.IsInRoleAsync(user, "SHOPPER", CancellationToken.None));
            Assert.Equal(["Merchant"], await roleStore.GetRolesAsync(user, CancellationToken.None));

            var inRole = await roleStore.GetUsersInRoleAsync("MERCHANT", CancellationToken.None);
            Assert.Contains(inRole, u => u.Id == user.Id);

            // Granting twice must not create a duplicate row.
            await roleStore.AddToRoleAsync(user, "MERCHANT", CancellationToken.None);
            Assert.Single(await roleStore.GetRolesAsync(user, CancellationToken.None));

            await roleStore.RemoveFromRoleAsync(user, "MERCHANT", CancellationToken.None);
            Assert.Empty(await roleStore.GetRolesAsync(user, CancellationToken.None));
            return true;
        });
    }

    [SkippableFact]
    public async Task Granting_a_role_that_does_not_exist_fails_loudly()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var user = NewUser(shop.TenantId, "badrole@example.test");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.WithServicesAsync(async services =>
            {
                var store = services.GetRequiredService<IUserStore<AppUser>>();
                await store.CreateAsync(user, CancellationToken.None);
                await ((IUserRoleStore<AppUser>)store)
                    .AddToRoleAsync(user, "NOSUCHROLE", CancellationToken.None);
                return true;
            }));
    }

    [SkippableFact]
    public async Task An_update_with_a_stale_concurrency_stamp_is_rejected()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var user = NewUser(shop.TenantId, "concurrent@example.test");

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();
            await store.CreateAsync(user, CancellationToken.None);

            var staleStamp = user.ConcurrencyStamp;

            user.DisplayName = "First writer";
            Assert.True((await store.UpdateAsync(user, CancellationToken.None)).Succeeded);

            // A second writer holding the stamp from before that update.
            user.ConcurrencyStamp = staleStamp;
            user.DisplayName = "Second writer";
            var stale = await store.UpdateAsync(user, CancellationToken.None);

            Assert.False(stale.Succeeded);
            Assert.Contains(stale.Errors, e => e.Code == "ConcurrencyFailure");
            return true;
        });

        var name = await Db.ScalarAsync<string>(
            Fixture, Primary, "SELECT DisplayName FROM dbo.Users WHERE Id = @id;", new { id = user.Id });

        Assert.Equal("First writer", name);
    }

    [SkippableFact]
    public async Task Deleting_a_user_takes_their_role_rows_with_them()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var user = NewUser(shop.TenantId, "gone@example.test");

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();
            await store.CreateAsync(user, CancellationToken.None);
            await ((IUserRoleStore<AppUser>)store).AddToRoleAsync(user, "SHOPPER", CancellationToken.None);

            Assert.Equal(1, await Db.CountAsync(Fixture, Primary, "UserRoles"));

            await store.DeleteAsync(user, CancellationToken.None);
            return true;
        });

        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "Users"));
        Assert.Equal(0, await Db.CountAsync(Fixture, Primary, "UserRoles"));

        // The roles themselves are reference data and must survive.
        Assert.Equal(2, await Db.CountAsync(Fixture, Primary, "Roles"));
    }

    [SkippableFact]
    public async Task Lockout_state_round_trips()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var user = NewUser(shop.TenantId, "locked@example.test");
        var until = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();
            var lockoutStore = (IUserLockoutStore<AppUser>)store;

            await store.CreateAsync(user, CancellationToken.None);

            await lockoutStore.SetLockoutEndDateAsync(user, until, CancellationToken.None);
            await lockoutStore.IncrementAccessFailedCountAsync(user, CancellationToken.None);
            await lockoutStore.IncrementAccessFailedCountAsync(user, CancellationToken.None);
            await store.UpdateAsync(user, CancellationToken.None);

            var reloaded = await store.FindByIdAsync(user.Id.ToString(), CancellationToken.None);

            Assert.Equal(2, reloaded!.AccessFailedCount);
            Assert.Equal(until, reloaded.LockoutEnd);
            return true;
        });
    }

    [SkippableFact]
    public async Task The_role_store_finds_the_seeded_roles()
    {
        RequireDatabase();

        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        await harness.WithServicesAsync(async services =>
        {
            var roleStore = services.GetRequiredService<IRoleStore<AppRole>>();

            var merchant = await roleStore.FindByNameAsync("MERCHANT", CancellationToken.None);
            var shopper = await roleStore.FindByNameAsync("SHOPPER", CancellationToken.None);

            Assert.NotNull(merchant);
            Assert.NotNull(shopper);
            Assert.Equal("Merchant", merchant!.Name);

            var byId = await roleStore.FindByIdAsync(merchant.Id.ToString(), CancellationToken.None);
            Assert.Equal(merchant.Id, byId!.Id);
            return true;
        });
    }

    [SkippableFact]
    public async Task A_shopper_account_carries_its_shop_and_customer()
    {
        RequireDatabase();

        // These become the shop_id and customer_id claims, so a signed-in
        // shopper's basket resolves without a further lookup.
        var shop = await TestData.SeedAsync(Fixture, Primary);
        await using var harness = TestHarness.SinglePrimary(Fixture, shop.TenantId);

        var user = NewUser(shop.TenantId, "shopper@example.test");
        user.ShopId = shop.ShopId;
        user.CustomerId = shop.CustomerId;

        await harness.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<IUserStore<AppUser>>();
            Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);

            var reloaded = await store.FindByIdAsync(user.Id.ToString(), CancellationToken.None);
            Assert.Equal(shop.ShopId, reloaded!.ShopId);
            Assert.Equal(shop.CustomerId, reloaded.CustomerId);
            return true;
        });
    }

    [SkippableFact]
    public async Task A_user_cannot_point_at_another_tenants_customer()
    {
        RequireDatabase();

        var tenantA = await TestData.SeedAsync(Fixture, Primary);
        var tenantB = await TestData.SeedAsync(Fixture, Primary);

        await using var harness = TestHarness.SinglePrimary(Fixture, tenantA.TenantId);

        // The composite foreign key (TenantId, CustomerId) makes this
        // impossible to store, not merely impossible through the application.
        var user = NewUser(tenantA.TenantId, "crosstenant@example.test");
        user.CustomerId = tenantB.CustomerId;

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            harness.WithServicesAsync(async services =>
            {
                var store = services.GetRequiredService<IUserStore<AppUser>>();
                return (await store.CreateAsync(user, CancellationToken.None)).Succeeded;
            }));
    }

    private static AppUser NewUser(Guid tenantId, string email) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        DisplayName = "Test User",
        PasswordHash = "hashed-password",
        SecurityStamp = Guid.NewGuid().ToString(),
        EmailConfirmed = true,
    };
}
