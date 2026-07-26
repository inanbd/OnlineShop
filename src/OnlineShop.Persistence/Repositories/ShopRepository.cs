using System.Data;
using Dapper;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Shops;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Repositories;

/// <summary>
/// Shop and shop-membership write repository.
/// </summary>
/// <remarks>
/// <code>
/// ShopRepository -> the caller's transaction -> WriteConnection -> primary
/// </code>
/// </remarks>
internal sealed class ShopRepository : IShopRepository
{
    private const string GetByIdSql = """
        SELECT
            s.Id,
            s.TenantId,
            s.Name,
            s.Slug,
            s.CurrencyCode,
            s.IsActive,
            s.CreatedAt,
            s.UpdatedAt
        FROM dbo.Shops AS s
        WHERE s.TenantId = @TenantId
          AND s.Id       = @ShopId;
        """;

    public async Task<Shop?> GetByIdAsync(
        Guid tenantId,
        Guid shopId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<ShopRow>(
            new CommandDefinition(
                GetByIdSql,
                new { TenantId = tenantId, ShopId = shopId },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    private const string SlugExistsSql = """
        SELECT CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Shops AS s
            WHERE s.TenantId = @TenantId
              AND s.Slug     = @Slug
        ) THEN 1 ELSE 0 END;
        """;

    public async Task<bool> SlugExistsAsync(
        Guid tenantId,
        string slug,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                SlugExistsSql,
                new { TenantId = tenantId, Slug = slug.Trim().ToLowerInvariant() },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string InsertSql = """
        INSERT INTO dbo.Shops
        (
            Id,
            TenantId,
            Name,
            Slug,
            CurrencyCode,
            IsActive,
            CreatedAt,
            UpdatedAt
        )
        VALUES
        (
            @Id,
            @TenantId,
            @Name,
            @Slug,
            @CurrencyCode,
            @IsActive,
            @CreatedAt,
            @UpdatedAt
        );
        """;

    public async Task InsertAsync(
        Shop shop,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shop);
        TenantGuard.Require(shop.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                new
                {
                    shop.Id,
                    shop.TenantId,
                    shop.Name,
                    shop.Slug,
                    shop.CurrencyCode,
                    shop.IsActive,
                    shop.CreatedAt,
                    shop.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string UpdateSql = """
        UPDATE dbo.Shops
        SET
            Name         = @Name,
            Slug         = @Slug,
            CurrencyCode = @CurrencyCode,
            IsActive     = @IsActive,
            UpdatedAt    = @UpdatedAt
        WHERE TenantId = @TenantId
          AND Id       = @Id;
        """;

    public async Task UpdateAsync(
        Shop shop,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shop);
        TenantGuard.Require(shop.TenantId);

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                UpdateSql,
                new
                {
                    shop.Id,
                    shop.TenantId,
                    shop.Name,
                    shop.Slug,
                    shop.CurrencyCode,
                    shop.IsActive,
                    shop.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new DbConcurrencyException(
                $"Shop '{shop.Id}' was not updated. It does not exist in this tenant.");
        }
    }

    private const string GetMemberByEmailSql = """
        SELECT
            m.Id,
            m.TenantId,
            m.ShopId,
            m.Email,
            m.Role,
            m.Status,
            m.InvitedByUserId,
            m.InvitedAt,
            m.UserId,
            m.AcceptedAt,
            m.CreatedAt,
            m.UpdatedAt
        FROM dbo.ShopMembers AS m
        WHERE m.TenantId = @TenantId
          AND m.ShopId   = @ShopId
          AND m.Email    = @Email;
        """;

    public async Task<ShopMember?> GetMemberByEmailAsync(
        Guid tenantId,
        Guid shopId,
        string email,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantId);

        var connection = transaction.RequireConnection();

        var row = await connection.QuerySingleOrDefaultAsync<ShopMemberRow>(
            new CommandDefinition(
                GetMemberByEmailSql,
                new
                {
                    TenantId = tenantId,
                    ShopId = shopId,
                    Email = email.Trim().ToLowerInvariant(),
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    private const string InsertMemberSql = """
        INSERT INTO dbo.ShopMembers
        (
            Id,
            TenantId,
            ShopId,
            Email,
            Role,
            Status,
            InvitedByUserId,
            InvitedAt,
            UserId,
            AcceptedAt,
            CreatedAt,
            UpdatedAt
        )
        VALUES
        (
            @Id,
            @TenantId,
            @ShopId,
            @Email,
            @Role,
            @Status,
            @InvitedByUserId,
            @InvitedAt,
            @UserId,
            @AcceptedAt,
            @CreatedAt,
            @UpdatedAt
        );
        """;

    public async Task InsertMemberAsync(
        ShopMember member,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        TenantGuard.Require(member.TenantId);

        var connection = transaction.RequireConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertMemberSql,
                new
                {
                    member.Id,
                    member.TenantId,
                    member.ShopId,
                    member.Email,
                    Role = (int)member.Role,
                    Status = (int)member.Status,
                    member.InvitedByUserId,
                    member.InvitedAt,
                    member.UserId,
                    member.AcceptedAt,
                    member.CreatedAt,
                    member.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string UpdateMemberSql = """
        UPDATE dbo.ShopMembers
        SET
            Role       = @Role,
            Status     = @Status,
            UserId     = @UserId,
            AcceptedAt = @AcceptedAt,
            UpdatedAt  = @UpdatedAt
        WHERE TenantId = @TenantId
          AND Id       = @Id;
        """;

    public async Task UpdateMemberAsync(
        ShopMember member,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        TenantGuard.Require(member.TenantId);

        var connection = transaction.RequireConnection();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                UpdateMemberSql,
                new
                {
                    member.Id,
                    member.TenantId,
                    Role = (int)member.Role,
                    Status = (int)member.Status,
                    member.UserId,
                    member.AcceptedAt,
                    member.UpdatedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new DbConcurrencyException(
                $"Shop member '{member.Id}' was not updated. They do not exist in this tenant.");
        }
    }

    /// <summary>Row shape of <see cref="GetByIdSql"/>.</summary>
    private sealed class ShopRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Slug { get; init; } = string.Empty;

        public string CurrencyCode { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public Shop ToDomain()
        {
            return Shop.Restore(
                id: Id,
                tenantId: TenantId,
                name: Name,
                slug: Slug,
                currencyCode: CurrencyCode,
                isActive: IsActive,
                createdAt: CreatedAt,
                updatedAt: UpdatedAt);
        }
    }

    /// <summary>Row shape of <see cref="GetMemberByEmailSql"/>.</summary>
    private sealed class ShopMemberRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public Guid ShopId { get; init; }

        public string Email { get; init; } = string.Empty;

        public int Role { get; init; }

        public int Status { get; init; }

        public Guid InvitedByUserId { get; init; }

        public DateTime InvitedAt { get; init; }

        public Guid? UserId { get; init; }

        public DateTime? AcceptedAt { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public ShopMember ToDomain()
        {
            return ShopMember.Restore(
                id: Id,
                tenantId: TenantId,
                shopId: ShopId,
                email: Email,
                role: (ShopMemberRole)Role,
                status: (ShopMemberStatus)Status,
                invitedByUserId: InvitedByUserId,
                invitedAt: InvitedAt,
                userId: UserId,
                acceptedAt: AcceptedAt,
                createdAt: CreatedAt,
                updatedAt: UpdatedAt);
        }
    }
}
