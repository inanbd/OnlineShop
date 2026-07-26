using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Api.Identity;
using OnlineShop.Api.Tenancy;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;

namespace OnlineShop.Api.Pages.Storefront;

/// <summary>
/// Shared behaviour for storefront pages: resolving which shop, and therefore
/// which tenant, the request is running in.
/// </summary>
/// <remarks>
/// <para>
/// A shopper arriving at <c>/shop/acme</c> has no tenant yet — resolving that
/// slug is what establishes one. <see cref="IShopDirectory"/> performs that
/// single deliberately-global lookup, and the result is published to
/// <see cref="ITenantContext"/> for the rest of the request. Every read and
/// write that follows is tenant-filtered as usual.
/// </para>
/// <para>
/// The slug comes from the URL, but the tenant does not: it comes from the
/// database row that slug resolved to, so a caller cannot name a tenant of
/// their choosing.
/// </para>
/// </remarks>
public abstract class StorefrontPageModel : PageModel
{
    protected StorefrontPageModel(ISender sender, IShopDirectory shopDirectory)
    {
        Sender = sender;
        ShopDirectory = shopDirectory;
    }

    protected ISender Sender { get; }

    protected IShopDirectory ShopDirectory { get; }

    public ShopDirectoryEntry? Shop { get; private set; }

    /// <summary>Which database served this page's data.</summary>
    public ReadConsistency Consistency { get; protected set; } = ReadConsistency.Eventual;

    /// <summary>The signed-in shopper's customer record, if they have one here.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>True when the visitor can actually put things in a basket.</summary>
    public bool CanShop => Shop is not null && CustomerId is not null;

    /// <summary>
    /// Resolves the storefront and puts its tenant in scope for the request.
    /// </summary>
    /// <returns>False when no active shop has that slug.</returns>
    protected async Task<bool> ResolveShopAsync(string slug, CancellationToken cancellationToken)
    {
        Shop = await ShopDirectory.FindBySlugAsync(slug, cancellationToken);

        if (Shop is null)
        {
            return false;
        }

        HttpContext.UseStorefrontTenant(Shop.TenantId);

        // A shopper only has a customer record in the shop they registered
        // with, so someone signed in elsewhere browses read-only.
        var signedInShop = User.GetShopId();
        CustomerId = signedInShop == Shop.ShopId ? User.GetCustomerId() : null;

        return true;
    }
}
