using System.Security.Claims;
using OnlineShop.Application.Abstractions;

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
    /// write any tenant's data simply by changing a request header.
    /// </remarks>
    public bool AllowHeaderTenantResolution { get; set; }
}

/// <summary>
/// Resolves the tenant for the current HTTP request.
/// </summary>
/// <remarks>
/// The tenant comes from the authenticated principal's <c>tenant_id</c> claim.
/// The header fallback exists so the API can be exercised locally without an
/// identity provider, and is gated behind configuration.
/// </remarks>
public sealed class HttpTenantContext : ITenantContext
{
    public const string TenantClaimType = "tenant_id";
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
                $"cannot proceed without a '{TenantClaimType}' claim on the authenticated principal.");

    private bool TryResolve(out Guid tenantId)
    {
        tenantId = Guid.Empty;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return false;
        }

        var claim = httpContext.User.FindFirstValue(TenantClaimType);
        if (Guid.TryParse(claim, out tenantId) && tenantId != Guid.Empty)
        {
            return true;
        }

        if (!_options.AllowHeaderTenantResolution)
        {
            return false;
        }

        var header = httpContext.Request.Headers[TenantHeaderName].ToString();
        return Guid.TryParse(header, out tenantId) && tenantId != Guid.Empty;
    }
}
