namespace OnlineShop.Application.Abstractions;

/// <summary>
/// The tenant the current request belongs to, resolved from the caller's
/// identity by the host.
/// </summary>
/// <remarks>
/// Handlers read <see cref="TenantId"/> and pass it explicitly into every query
/// and repository call, so that tenant scoping is visible in the code rather
/// than hidden in ambient state. Persistence then requires it in the SQL.
/// </remarks>
public interface ITenantContext
{
    /// <summary>The resolved tenant. Throws when no tenant is in scope.</summary>
    Guid TenantId { get; }

    bool HasTenant { get; }
}
