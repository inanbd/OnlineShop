using Dapper;
using Microsoft.AspNetCore.Identity;
using OnlineShop.Application.Abstractions.Identity;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Identity;

/// <summary>
/// ASP.NET Core Identity's role store, over Dapper.
/// </summary>
/// <remarks>
/// Roles are reference data seeded by <c>database/schema.sql</c> and are global
/// rather than tenant-scoped: "Merchant" and "Shopper" describe the kind of
/// account, not anything a tenant owns. The write methods exist because
/// <c>RoleManager</c> requires them, but nothing in the application calls them.
/// Like the user store, this reads the primary — an authorisation decision must
/// not be made from a stale replica.
/// </remarks>
internal sealed class DapperRoleStore : IRoleStore<AppRole>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DapperRoleStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string SelectColumns = """
        SELECT r.Id, r.Name, r.NormalizedName, r.ConcurrencyStamp
        FROM dbo.Roles AS r
        """;

    public Task<string> GetRoleIdAsync(AppRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Id.ToString());

    public Task<string?> GetRoleNameAsync(AppRole role, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(role.Name);

    public Task SetRoleNameAsync(AppRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(AppRole role, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(AppRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    private const string InsertSql = """
        INSERT INTO dbo.Roles (Id, Name, NormalizedName, ConcurrencyStamp)
        VALUES (@Id, @Name, @NormalizedName, @ConcurrencyStamp);
        """;

    public async Task<IdentityResult> CreateAsync(AppRole role, CancellationToken cancellationToken)
    {
        if (role.Id == Guid.Empty)
        {
            role.Id = Guid.NewGuid();
        }

        role.ConcurrencyStamp = Guid.NewGuid().ToString();

        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(InsertSql, role, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return IdentityResult.Success;
    }

    private const string UpdateSql = """
        UPDATE dbo.Roles
        SET Name = @Name, NormalizedName = @NormalizedName, ConcurrencyStamp = @ConcurrencyStamp
        WHERE Id = @Id;
        """;

    public async Task<IdentityResult> UpdateAsync(AppRole role, CancellationToken cancellationToken)
    {
        role.ConcurrencyStamp = Guid.NewGuid().ToString();

        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(UpdateSql, role, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return IdentityResult.Success;
    }

    private const string DeleteSql = "DELETE FROM dbo.Roles WHERE Id = @Id;";

    public async Task<IdentityResult> DeleteAsync(AppRole role, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(DeleteSql, new { role.Id }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return IdentityResult.Success;
    }

    private const string FindByIdSql = $"{SelectColumns} WHERE r.Id = @Id;";

    public async Task<AppRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(roleId, out var id))
        {
            return null;
        }

        return await QuerySingleAsync(FindByIdSql, new { Id = id }, cancellationToken).ConfigureAwait(false);
    }

    private const string FindByNameSql = $"{SelectColumns} WHERE r.NormalizedName = @NormalizedName;";

    public Task<AppRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
        QuerySingleAsync(FindByNameSql, new { NormalizedName = normalizedRoleName }, cancellationToken);

    private async Task<AppRole?> QuerySingleAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<AppRole>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        // Connections are opened and disposed per operation; nothing is held.
    }
}
