using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Shops;

public enum ShopMemberRole
{
    Viewer = 0,
    Staff = 1,
    Manager = 2,
    Owner = 3,
}

public enum ShopMemberStatus
{
    Invited = 0,
    Active = 1,
    Revoked = 2,
}

/// <summary>
/// A user granted access to a shop, tracked from invitation through acceptance.
/// </summary>
public sealed class ShopMember : ITenantOwned
{
    private ShopMember(
        Guid id,
        Guid tenantId,
        Guid shopId,
        string email,
        ShopMemberRole role,
        ShopMemberStatus status,
        Guid invitedByUserId,
        DateTime invitedAt,
        Guid? userId,
        DateTime? acceptedAt,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        ShopId = shopId;
        Email = email;
        Role = role;
        Status = status;
        InvitedByUserId = invitedByUserId;
        InvitedAt = invitedAt;
        UserId = userId;
        AcceptedAt = acceptedAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ShopId { get; }

    public string Email { get; }

    public ShopMemberRole Role { get; private set; }

    public ShopMemberStatus Status { get; private set; }

    public Guid InvitedByUserId { get; }

    public DateTime InvitedAt { get; }

    public Guid? UserId { get; private set; }

    public DateTime? AcceptedAt { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public static ShopMember Invite(
        Guid tenantId,
        Guid shopId,
        string email,
        ShopMemberRole role,
        Guid invitedByUserId,
        DateTime utcNow)
    {
        return new ShopMember(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            shopId: Guard.AgainstEmpty(shopId),
            email: NormalizeEmail(email),
            role: role,
            status: ShopMemberStatus.Invited,
            invitedByUserId: Guard.AgainstEmpty(invitedByUserId),
            invitedAt: utcNow,
            userId: null,
            acceptedAt: null,
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    public static ShopMember Restore(
        Guid id,
        Guid tenantId,
        Guid shopId,
        string email,
        ShopMemberRole role,
        ShopMemberStatus status,
        Guid invitedByUserId,
        DateTime invitedAt,
        Guid? userId,
        DateTime? acceptedAt,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new ShopMember(
            id,
            tenantId,
            shopId,
            email,
            role,
            status,
            invitedByUserId,
            invitedAt,
            userId,
            acceptedAt,
            createdAt,
            updatedAt);
    }

    public void Accept(Guid userId, DateTime utcNow)
    {
        if (Status != ShopMemberStatus.Invited)
        {
            throw new DomainException($"Only an invited member can accept an invitation (current status: {Status}).");
        }

        UserId = Guard.AgainstEmpty(userId);
        Status = ShopMemberStatus.Active;
        AcceptedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void ChangeRole(ShopMemberRole role, DateTime utcNow)
    {
        if (Status == ShopMemberStatus.Revoked)
        {
            throw new DomainException("A revoked member must be reinstated before their role can change.");
        }

        Role = role;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Returns a revoked member to the invited state, reusing the existing row
    /// so the membership history is preserved.
    /// </summary>
    public void Reinstate(ShopMemberRole role, DateTime utcNow)
    {
        if (Status != ShopMemberStatus.Revoked)
        {
            throw new DomainException($"Only a revoked member can be reinstated (current status: {Status}).");
        }

        Role = role;
        Status = ShopMemberStatus.Invited;
        UserId = null;
        AcceptedAt = null;
        UpdatedAt = utcNow;
    }

    public void Revoke(DateTime utcNow)
    {
        Status = ShopMemberStatus.Revoked;
        UpdatedAt = utcNow;
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = Guard.AgainstNullOrWhiteSpace(email).ToLowerInvariant();
        if (!normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException("A shop member email address must contain '@'.");
        }

        return normalized;
    }
}
