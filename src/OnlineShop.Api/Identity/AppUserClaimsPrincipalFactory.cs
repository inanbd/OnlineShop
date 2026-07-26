using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Abstractions.Identity;

namespace OnlineShop.Api.Identity;

/// <summary>
/// Adds the tenant claim that every query and command is scoped by.
/// </summary>
/// <remarks>
/// <see cref="ITenantContext"/> reads <c>tenant_id</c> from the signed-in
/// principal, so this factory is what connects authentication to tenant
/// isolation. Shoppers additionally carry their shop and customer identifiers,
/// so their cart resolves without a further lookup on every request.
/// </remarks>
public sealed class AppUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<AppUser, AppRole>
{
    public AppUserClaimsPrincipalFactory(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(AppClaimTypes.TenantId, user.TenantId.ToString()));

        if (user.ShopId is { } shopId)
        {
            identity.AddClaim(new Claim(AppClaimTypes.ShopId, shopId.ToString()));
        }

        if (user.CustomerId is { } customerId)
        {
            identity.AddClaim(new Claim(AppClaimTypes.CustomerId, customerId.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(ClaimTypes.GivenName, user.DisplayName));
        }

        return identity;
    }
}

/// <summary>Reads the extra claims back off a signed-in principal.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? GetTenantId(this ClaimsPrincipal principal) =>
        ParseGuid(principal.FindFirstValue(AppClaimTypes.TenantId));

    public static Guid? GetShopId(this ClaimsPrincipal principal) =>
        ParseGuid(principal.FindFirstValue(AppClaimTypes.ShopId));

    public static Guid? GetCustomerId(this ClaimsPrincipal principal) =>
        ParseGuid(principal.FindFirstValue(AppClaimTypes.CustomerId));

    public static string DisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.GivenName)
        ?? principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.Identity?.Name
        ?? "Account";

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed : null;
}
