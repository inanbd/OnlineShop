using Dapper;
using Microsoft.AspNetCore.Identity;
using OnlineShop.Application.Abstractions.Identity;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Identity;

/// <summary>
/// ASP.NET Core Identity's user store, over Dapper.
/// </summary>
/// <remarks>
/// <para>
/// Identity ships an Entity Framework store by default. Rule 1 rules that out,
/// so the store interfaces are implemented directly against the same
/// connection factory the rest of the persistence layer uses.
/// </para>
/// <para>
/// <b>Every operation here, including reads, uses the write connection.</b>
/// That is a deliberate exception to "queries read the replica", and it is not
/// a performance oversight: authentication decides whether someone gets in.
/// A replica that is thirty seconds behind would happily authenticate against
/// a password that has just been changed, or let in an account that has just
/// been locked out. Staleness in a security principal is a security bug, not a
/// stale-UI annoyance.
/// </para>
/// <para>
/// These calls run during authentication, outside any MediatR request, so the
/// CQRS scope is <c>Unspecified</c> and the guard permits either connection.
/// The choice is made explicitly here rather than being left to the guard.
/// </para>
/// </remarks>
internal sealed class DapperUserStore :
    IUserStore<AppUser>,
    IUserPasswordStore<AppUser>,
    IUserEmailStore<AppUser>,
    IUserSecurityStampStore<AppUser>,
    IUserRoleStore<AppUser>,
    IUserLockoutStore<AppUser>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DapperUserStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string SelectColumns = """
        SELECT
            u.Id,
            u.TenantId,
            u.Email,
            u.NormalizedEmail,
            u.UserName,
            u.NormalizedUserName,
            u.DisplayName,
            u.PasswordHash,
            u.SecurityStamp,
            u.ConcurrencyStamp,
            u.EmailConfirmed,
            u.LockoutEnabled,
            u.LockoutEnd,
            u.AccessFailedCount,
            u.ShopId,
            u.CustomerId,
            u.CreatedAt,
            u.UpdatedAt
        FROM dbo.Users AS u
        """;

    // ---------------- IUserStore ----------------

    public Task<string> GetUserIdAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    private const string InsertSql = """
        INSERT INTO dbo.Users
        (
            Id, TenantId, Email, NormalizedEmail, UserName, NormalizedUserName, DisplayName,
            PasswordHash, SecurityStamp, ConcurrencyStamp, EmailConfirmed, LockoutEnabled,
            LockoutEnd, AccessFailedCount, ShopId, CustomerId, CreatedAt, UpdatedAt
        )
        VALUES
        (
            @Id, @TenantId, @Email, @NormalizedEmail, @UserName, @NormalizedUserName, @DisplayName,
            @PasswordHash, @SecurityStamp, @ConcurrencyStamp, @EmailConfirmed, @LockoutEnabled,
            @LockoutEnd, @AccessFailedCount, @ShopId, @CustomerId, @CreatedAt, @UpdatedAt
        );
        """;

    public async Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        TenantGuard.Require(user.TenantId);

        if (user.Id == Guid.Empty)
        {
            user.Id = Guid.NewGuid();
        }

        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        user.CreatedAt = user.CreatedAt == default ? DateTime.UtcNow : user.CreatedAt;
        user.UpdatedAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(InsertSql, user, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (IsUniqueViolation(exception))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = "An account with that email address already exists.",
            });
        }

        return IdentityResult.Success;
    }

    /// <remarks>
    /// The ConcurrencyStamp predicate gives optimistic concurrency: two admins
    /// editing the same account will not silently overwrite one another.
    /// </remarks>
    private const string UpdateSql = """
        UPDATE dbo.Users
        SET
            Email              = @Email,
            NormalizedEmail    = @NormalizedEmail,
            UserName           = @UserName,
            NormalizedUserName = @NormalizedUserName,
            DisplayName        = @DisplayName,
            PasswordHash       = @PasswordHash,
            SecurityStamp      = @SecurityStamp,
            ConcurrencyStamp   = @NewConcurrencyStamp,
            EmailConfirmed     = @EmailConfirmed,
            LockoutEnabled     = @LockoutEnabled,
            LockoutEnd         = @LockoutEnd,
            AccessFailedCount  = @AccessFailedCount,
            ShopId             = @ShopId,
            CustomerId         = @CustomerId,
            UpdatedAt          = @UpdatedAt
        WHERE TenantId         = @TenantId
          AND Id               = @Id
          AND ConcurrencyStamp = @ConcurrencyStamp;
        """;

    public async Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        TenantGuard.Require(user.TenantId);

        var newStamp = Guid.NewGuid().ToString();
        user.UpdatedAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                UpdateSql,
                new
                {
                    user.Id,
                    user.TenantId,
                    user.Email,
                    user.NormalizedEmail,
                    user.UserName,
                    user.NormalizedUserName,
                    user.DisplayName,
                    user.PasswordHash,
                    user.SecurityStamp,
                    user.ConcurrencyStamp,
                    NewConcurrencyStamp = newStamp,
                    user.EmailConfirmed,
                    user.LockoutEnabled,
                    user.LockoutEnd,
                    user.AccessFailedCount,
                    user.ShopId,
                    user.CustomerId,
                    user.UpdatedAt,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "ConcurrencyFailure",
                Description = "This account was changed by someone else. Reload and try again.",
            });
        }

        user.ConcurrencyStamp = newStamp;
        return IdentityResult.Success;
    }

    /// <remarks>
    /// Kept separate from the user delete so each can be judged on its own.
    /// This one cannot be tenant-filtered — <c>UserRoles</c> has no
    /// <c>TenantId</c>, because roles are global reference data. It is keyed by
    /// the globally unique user id, which is what makes that safe.
    /// </remarks>
    private const string DeleteUserRolesSql = """
        DELETE FROM dbo.UserRoles WHERE UserId = @Id;
        """;

    private const string DeleteUserSql = """
        DELETE FROM dbo.Users WHERE TenantId = @TenantId AND Id = @Id;
        """;

    public async Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        TenantGuard.Require(user.TenantId);

        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                DeleteUserRolesSql,
                new { user.Id },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                DeleteUserSql,
                new { user.Id, user.TenantId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return IdentityResult.Success;
    }

    private const string FindByIdSql = $"{SelectColumns} WHERE u.Id = @Id;";

    public async Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            return null;
        }

        return await QuerySingleAsync(FindByIdSql, new { Id = id }, cancellationToken).ConfigureAwait(false);
    }

    private const string FindByNameSql = $"{SelectColumns} WHERE u.NormalizedUserName = @NormalizedUserName;";

    public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        QuerySingleAsync(FindByNameSql, new { NormalizedUserName = normalizedUserName }, cancellationToken);

    // ---------------- IUserPasswordStore ----------------

    public Task SetPasswordHashAsync(AppUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // ---------------- IUserEmailStore ----------------

    public Task SetEmailAsync(AppUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.Email);

    public Task<bool> GetEmailConfirmedAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(AppUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    private const string FindByEmailSql = $"{SelectColumns} WHERE u.NormalizedEmail = @NormalizedEmail;";

    public Task<AppUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        QuerySingleAsync(FindByEmailSql, new { NormalizedEmail = normalizedEmail }, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(AppUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    // ---------------- IUserSecurityStampStore ----------------

    public Task SetSecurityStampAsync(AppUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    // ---------------- IUserRoleStore ----------------

    private const string AddToRoleSql = """
        INSERT INTO dbo.UserRoles (UserId, RoleId)
        SELECT @UserId, r.Id
        FROM dbo.Roles AS r
        WHERE r.NormalizedName = @NormalizedName
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.UserRoles AS ur
              WHERE ur.UserId = @UserId
                AND ur.RoleId = r.Id
          );
        """;

    public async Task AddToRoleAsync(AppUser user, string roleName, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                AddToRoleSql,
                new { UserId = user.Id, NormalizedName = roleName.ToUpperInvariant() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            // Either the role does not exist or the user already had it. The
            // second is harmless; the first is a configuration error worth
            // surfacing rather than silently ignoring.
            var exists = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Roles WHERE NormalizedName = @NormalizedName) THEN 1 ELSE 0 END;",
                    new { NormalizedName = roleName.ToUpperInvariant() },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (!exists)
            {
                throw new InvalidOperationException(
                    $"Role '{roleName}' does not exist. Roles are reference data seeded by database/schema.sql.");
            }
        }
    }

    private const string RemoveFromRoleSql = """
        DELETE ur
        FROM dbo.UserRoles AS ur
        INNER JOIN dbo.Roles AS r ON r.Id = ur.RoleId
        WHERE ur.UserId = @UserId
          AND r.NormalizedName = @NormalizedName;
        """;

    public async Task RemoveFromRoleAsync(AppUser user, string roleName, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                RemoveFromRoleSql,
                new { UserId = user.Id, NormalizedName = roleName.ToUpperInvariant() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string GetRolesSql = """
        SELECT r.Name
        FROM dbo.UserRoles AS ur
        INNER JOIN dbo.Roles AS r ON r.Id = ur.RoleId
        WHERE ur.UserId = @UserId
        ORDER BY r.Name;
        """;

    public async Task<IList<string>> GetRolesAsync(AppUser user, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var roles = await connection.QueryAsync<string>(
            new CommandDefinition(
                GetRolesSql,
                new { UserId = user.Id },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return roles.ToList();
    }

    private const string IsInRoleSql = """
        SELECT CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.UserRoles AS ur
            INNER JOIN dbo.Roles AS r ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId
              AND r.NormalizedName = @NormalizedName
        ) THEN 1 ELSE 0 END;
        """;

    public async Task<bool> IsInRoleAsync(AppUser user, string roleName, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                IsInRoleSql,
                new { UserId = user.Id, NormalizedName = roleName.ToUpperInvariant() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string GetUsersInRoleSql = $"""
        {SelectColumns}
        INNER JOIN dbo.UserRoles AS ur ON ur.UserId = u.Id
        INNER JOIN dbo.Roles AS r ON r.Id = ur.RoleId
        WHERE r.NormalizedName = @NormalizedName
        ORDER BY u.Email;
        """;

    public async Task<IList<AppUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var users = await connection.QueryAsync<AppUser>(
            new CommandDefinition(
                GetUsersInRoleSql,
                new { NormalizedName = roleName.ToUpperInvariant() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return users.ToList();
    }

    // ---------------- IUserLockoutStore ----------------

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(AppUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(AppUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(AppUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(AppUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    // ---------------- helpers ----------------

    private async Task<AppUser?> QuerySingleAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateWriteConnection();
        await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<AppUser>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(Microsoft.Data.SqlClient.SqlException exception) =>
        exception.Number is 2601 or 2627;

    public void Dispose()
    {
        // Connections are opened and disposed per operation; nothing is held.
    }
}
