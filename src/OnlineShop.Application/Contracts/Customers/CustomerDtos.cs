namespace OnlineShop.Application.Contracts.Customers;

/// <summary>Row projection for customer listing screens.</summary>
public sealed class CustomerListItemDto
{
    public Guid Id { get; init; }

    public Guid ShopId { get; init; }

    public string Email { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public int OrderCount { get; init; }

    public decimal LifetimeValue { get; init; }

    public DateTime? LastOrderAt { get; init; }

    public DateTime CreatedAt { get; init; }
}

/// <summary>Filter for customer listing. Every field is applied as a parameter.</summary>
public sealed record CustomerFilter
{
    public Guid? ShopId { get; init; }

    /// <summary>Free-text match against email and full name.</summary>
    public string? SearchTerm { get; init; }

    public DateTime? CreatedFromUtc { get; init; }

    public DateTime? CreatedToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
