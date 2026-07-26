using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Catalog;

public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}

/// <summary>
/// A sellable catalog item belonging to a shop within a tenant.
/// </summary>
public sealed class Product : ITenantOwned
{
    private Product(
        Guid id,
        Guid tenantId,
        Guid shopId,
        string name,
        string sku,
        string? description,
        decimal price,
        string currencyCode,
        ProductStatus status,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? deletedAt)
    {
        Id = id;
        TenantId = tenantId;
        ShopId = shopId;
        Name = name;
        Sku = sku;
        Description = description;
        Price = price;
        CurrencyCode = currencyCode;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        DeletedAt = deletedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ShopId { get; }

    public string Name { get; private set; }

    public string Sku { get; private set; }

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    public string CurrencyCode { get; private set; }

    public ProductStatus Status { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt.HasValue;

    public static Product Create(
        Guid tenantId,
        Guid shopId,
        string name,
        string sku,
        string? description,
        decimal price,
        string currencyCode,
        DateTime utcNow)
    {
        return new Product(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            shopId: Guard.AgainstEmpty(shopId),
            name: Guard.AgainstNullOrWhiteSpace(name),
            sku: NormalizeSku(sku),
            description: string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            price: Guard.AgainstNegative(price),
            currencyCode: NormalizeCurrency(currencyCode),
            status: ProductStatus.Draft,
            createdAt: utcNow,
            updatedAt: utcNow,
            deletedAt: null);
    }

    public static Product Restore(
        Guid id,
        Guid tenantId,
        Guid shopId,
        string name,
        string sku,
        string? description,
        decimal price,
        string currencyCode,
        ProductStatus status,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? deletedAt)
    {
        return new Product(
            id,
            tenantId,
            shopId,
            name,
            sku,
            description,
            price,
            currencyCode,
            status,
            createdAt,
            updatedAt,
            deletedAt);
    }

    public void UpdateDetails(
        string name,
        string sku,
        string? description,
        decimal price,
        DateTime utcNow)
    {
        EnsureNotDeleted();

        Name = Guard.AgainstNullOrWhiteSpace(name);
        Sku = NormalizeSku(sku);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = Guard.AgainstNegative(price);
        UpdatedAt = utcNow;
    }

    public void Publish(DateTime utcNow)
    {
        EnsureNotDeleted();

        Status = ProductStatus.Active;
        UpdatedAt = utcNow;
    }

    public void Archive(DateTime utcNow)
    {
        EnsureNotDeleted();

        Status = ProductStatus.Archived;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Soft-deletes the product. Orders reference historical products, so rows
    /// are retained and filtered out of the catalog instead of being removed.
    /// </summary>
    public void Delete(DateTime utcNow)
    {
        if (IsDeleted)
        {
            return;
        }

        Status = ProductStatus.Archived;
        DeletedAt = utcNow;
        UpdatedAt = utcNow;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new DomainException($"Product '{Id}' has been deleted and can no longer be modified.");
        }
    }

    private static string NormalizeSku(string sku)
    {
        return Guard.AgainstNullOrWhiteSpace(sku).ToUpperInvariant();
    }

    private static string NormalizeCurrency(string currencyCode)
    {
        var normalized = Guard.AgainstNullOrWhiteSpace(currencyCode).ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new DomainException("Currency code must be a three-letter ISO 4217 code.");
        }

        return normalized;
    }
}
