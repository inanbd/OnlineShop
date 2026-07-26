using OnlineShop.Domain.Shops;

namespace OnlineShop.Application.Contracts.Shops;

public sealed class ShopListItemDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int ProductCount { get; init; }

    public int OrderCount { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class ShopMemberDto
{
    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public ShopMemberRole Role { get; init; }

    public ShopMemberStatus Status { get; init; }

    public DateTime InvitedAt { get; init; }

    public DateTime? AcceptedAt { get; init; }
}
