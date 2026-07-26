using System.Data;
using OnlineShop.Domain.Tenants;

namespace OnlineShop.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Write side of tenant provisioning. Implemented by <c>TenantRepository</c>.
/// </summary>
/// <remarks>
/// The only repository whose statements are not filtered by an existing
/// <c>TenantId</c>, because it is the one that brings a tenant into being.
/// Slug uniqueness is checked across the whole installation, since a tenant
/// slug is what distinguishes one tenant from another.
/// </remarks>
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(
        Guid tenantId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(
        string slug,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        Tenant tenant,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}
