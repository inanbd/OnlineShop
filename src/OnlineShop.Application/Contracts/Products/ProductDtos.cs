using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Contracts.Products;

/// <summary>Full product projection returned by <c>GetProductByIdQuery</c>.</summary>
public sealed class ProductDto
{
    public Guid Id { get; init; }

    public Guid ShopId { get; init; }

    public string ShopName { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string? Description { get; init; }

    public decimal Price { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public ProductStatus Status { get; init; }

    public int QuantityOnHand { get; init; }

    public int QuantityReserved { get; init; }

    public int QuantityAvailable { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}

/// <summary>Row projection for product listing screens.</summary>
public sealed class ProductListItemDto
{
    public Guid Id { get; init; }

    public Guid ShopId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public ProductStatus Status { get; init; }

    public int QuantityAvailable { get; init; }

    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Filter for product listing. All fields are optional; every one of them is
/// applied as a parameterised predicate, never string-concatenated.
/// </summary>
public sealed record ProductFilter
{
    public Guid? ShopId { get; init; }

    /// <summary>Free-text match against name and SKU.</summary>
    public string? SearchTerm { get; init; }

    public ProductStatus? Status { get; init; }

    public decimal? MinPrice { get; init; }

    public decimal? MaxPrice { get; init; }

    /// <summary>Excludes soft-deleted rows unless explicitly set to true.</summary>
    public bool IncludeDeleted { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
