using System.Security.Claims;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Identity;

namespace OnlineShop.Api.Tenancy;

public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>
    /// Whether an <c>X-Tenant-Id</c> header may establish the tenant.
    /// </summary>
    /// <remarks>
    /// Off by default, and it must stay off outside development. A header is
    /// caller-supplied: trusting it in production would let anyone read and
    /// write any tenant's data simply by changing a request header. Now that
    /// sign-in issues a real tenant claim, this exists only for exercising the
    /// JSON API without a browser session.
    /// </remarks>
    public bool AllowHeaderTenantResolution { get; set; }
}

/// <summary>
/// Resolves the tenant for the current HTTP request.
/// </summary>
/// <remarks>
/// <para>There are two legitimate sources, checked in this order:</para>
/// <list type="number">
/// <item>
/// <b>The storefront being browsed.</b> A shopper at <c>/shop/acme</c> is
/// acting within that shop's tenant whether or not they are signed in, so
/// storefront pages resolve the slug and set this for the request. The value
/// comes from a database lookup performed on the server, never from the caller.
/// </item>
/// <item>
/// <b>The signed-in principal's <c>tenant_id</c> claim.</b> This is what the
/// back-office runs on.
/// </item>
/// </list>
/// <para>
/// The storefront takes precedence deliberately: a merchant signed in to their
/// own tenant who visits someone else's public shop is browsing that shop's
/// catalog, and every read must be scoped accordingly. Only public storefront
/// data is reachable that way — the back-office is behind a role check that
/// uses the claim, not the route.
/// </para>
/// </remarks>
public sealed class HttpTenantContext : ITenantContext
{
    /// <summary>Key under which storefront pages publish the resolved tenant.</summary>
    internal const string StorefrontTenantItemKey = "OnlineShop.StorefrontTenantId";

    public const string TenantHeaderName = "X-Tenant-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenancyOptions _options;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor, TenancyOptions options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    public bool HasTenant => TryResolve(out _);

    public Guid TenantId =>
        TryResolve(out var tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                "No tenant is in scope for this request. Every read and write is tenant-scoped, so the request " +
                $"cannot proceed without a '{AppClaimTypes.TenantId}' claim on the signed-in principal, or a " +
                "storefront shop resolved from the URL.");

    private bool TryResolve(out Guid tenantId)
    {
        tenantId = Guid.Empty;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return false;
        }

        // 1. The storefront being browsed, resolved server-side from the slug.
        if (httpContext.Items.TryGetValue(StorefrontTenantItemKey, out var storefront)
            && storefront is Guid storefrontTenantId
            && storefrontTenantId != Guid.Empty)
        {
            tenantId = storefrontTenantId;
            return true;
        }

        // 2. The signed-in principal.
        var claim = httpContext.User.FindFirstValue(AppClaimTypes.TenantId);
        if (Guid.TryParse(claim, out tenantId) && tenantId != Guid.Empty)
        {
            return true;
        }

        // 3. Development-only header, for driving the JSON API without a session.
        if (!_options.AllowHeaderTenantResolution)
        {
            return false;
        }

        var header = httpContext.Request.Headers[TenantHeaderName].ToString();
        return Guid.TryParse(header, out tenantId) && tenantId != Guid.Empty;
    }
}

/// <summary>
/// Lets storefront pages publish the tenant they resolved from the URL.
/// </summary>
public static class StorefrontTenantExtensions
{
    public static void UseStorefrontTenant(this HttpContext httpContext, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A storefront must resolve to a real tenant.", nameof(tenantId));
        }

        httpContext.Items[HttpTenantContext.StorefrontTenantItemKey] = tenantId;
    }
}
