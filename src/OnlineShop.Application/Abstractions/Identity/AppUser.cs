namespace OnlineShop.Application.Abstractions.Identity;

/// <summary>
/// An account that can sign in.
/// </summary>
/// <remarks>
/// <para>
/// A plain mutable POCO, because that is the shape ASP.NET Core Identity works
/// with: <c>UserManager</c> mutates the object through the store's setters and
/// then calls <c>UpdateAsync</c> to persist it. Keeping it out of the domain
/// leaves the domain free of authentication concerns.
/// </para>
/// <para>
/// Every user belongs to exactly one tenant. Shoppers additionally carry the
/// shop and customer they map onto, so a signed-in shopper's cart and orders
/// resolve without a second lookup.
/// </para>
/// </remarks>
public sealed class AppUser
{
    public Guid Id { get; set; }

    /// <summary>The tenant this account acts within. Becomes the <c>tenant_id</c> claim.</summary>
    public Guid TenantId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string NormalizedUserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public bool EmailConfirmed { get; set; }

    public bool LockoutEnabled { get; set; } = true;

    public DateTimeOffset? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    /// <summary>Set for shoppers; null for merchants.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Set for shoppers; null for merchants.</summary>
    public Guid? CustomerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>A sign-in role. Global, not tenant-scoped.</summary>
public sealed class AppRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string? ConcurrencyStamp { get; set; }
}

/// <summary>
/// The two kinds of account.
/// </summary>
/// <remarks>
/// Deliberately coarse. Fine-grained authority within a shop is
/// <see cref="Domain.Shops.ShopMemberRole"/>, which is per-shop data rather
/// than an attribute of the sign-in itself.
/// </remarks>
public static class AppRoles
{
    public const string Merchant = "Merchant";

    public const string Shopper = "Shopper";

    public static readonly IReadOnlyList<string> All = [Merchant, Shopper];
}

/// <summary>Claim types this application issues beyond the standard set.</summary>
public static class AppClaimTypes
{
    /// <summary>Read by <c>ITenantContext</c> to scope every query and command.</summary>
    public const string TenantId = "tenant_id";

    public const string ShopId = "shop_id";

    public const string CustomerId = "customer_id";
}
