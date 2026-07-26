using System.Data;
using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Tenants;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Repositories;

/// <summary>
/// Tenant provisioning write repository.
/// </summary>
/// <remarks>
/// <code>
/// TenantRepository -> the caller's transaction -> WriteConnection -> primary
/// </code>
/// <para>
/// The one repository that does not filter by an ambient tenant, because it
/// creates the tenant. The insert still names its parameter <c>@TenantId</c>:
/// the row's <c>Id</c> <em>is</em> the tenant identifier that every other table
/// then points at.
/// </para>
/// </remarks>
internal sealed class TenantRepository : ITenantRepository
{
    private const string GetByIdSql = """
        SELECT t.Id, t.Name, t.Slug, t.CreatedAt, t.UpdatedAt
        FROM dbo.Tenants AS t
        WHERE t.Id = @TenantId;
        """;

    public async Task<Tenant?> GetByIdAsync(
        Guid tenantId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<TenantRow>(
            new CommandDefinition(
                GetByIdSql,
                new { TenantId = tenantId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <remarks>
    /// Deliberately global. A tenant slug identifies the tenant itself, so it
    /// cannot be scoped to one.
    /// </remarks>
    private const string SlugExistsSql = """
        SELECT CASE WHEN EXISTS
        (
            SELECT 1 FROM dbo.Tenants AS t WHERE t.Slug = @Slug
        ) THEN 1 ELSE 0 END;
        """;

    public async Task<bool> SlugExistsAsync(
        string slug,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var connection = transaction.RequireConnection();

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                SlugExistsSql,
                new { Slug = slug.Trim().ToLowerInvariant() },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string InsertSql = """
        INSERT INTO dbo.Tenants (Id, Name, Slug, CreatedAt, UpdatedAt)
        VALUES (@TenantId, @Name, @Slug, @CreatedAt, @UpdatedAt);
        """;

    public async Task InsertAsync(
        Tenant tenant,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        TenantGuard.Require(tenant.Id);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                new
                {
                    TenantId = tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.CreatedAt,
                    tenant.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class TenantRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Slug { get; init; } = string.Empty;

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public Tenant ToDomain() => Tenant.Restore(Id, Name, Slug, CreatedAt, UpdatedAt);
    }
}
