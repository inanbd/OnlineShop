namespace OnlineShop.Domain.Common;

/// <summary>
/// Implemented by every aggregate that belongs to a tenant.
/// Persistence uses this to guarantee that no row is ever read or written
/// without a <c>TenantId</c> filter.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
