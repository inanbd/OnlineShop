using System.Runtime.CompilerServices;

namespace OnlineShop.Persistence.Internal;

/// <summary>
/// Last line of defence for tenant isolation.
/// </summary>
/// <remarks>
/// Every SQL statement in this layer filters on <c>TenantId = @TenantId</c>. An
/// empty GUID would still be a valid parameter value and would simply match
/// nothing — or, in a statement written slightly wrong, match everything. This
/// turns that into an immediate, obvious failure instead.
/// </remarks>
internal static class TenantGuard
{
    public static Guid Require(Guid tenantId, [CallerMemberName] string? operation = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"'{operation}' was called without a tenant. Every tenant-scoped read and write must carry a " +
                "resolved TenantId; an empty one means the tenant was never established for this request.");
        }

        return tenantId;
    }
}
