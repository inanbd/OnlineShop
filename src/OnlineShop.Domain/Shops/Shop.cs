using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Shops;

/// <summary>
/// A storefront owned by a tenant. A tenant may own several shops.
/// </summary>
public sealed class Shop : ITenantOwned
{
    private Shop(
        Guid id,
        Guid tenantId,
        string name,
        string slug,
        string currencyCode,
        bool isActive,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Slug = slug;
        CurrencyCode = currencyCode;
        IsActive = isActive;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public string CurrencyCode { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public static Shop Create(
        Guid tenantId,
        string name,
        string slug,
        string currencyCode,
        DateTime utcNow)
    {
        return new Shop(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            name: Guard.AgainstNullOrWhiteSpace(name),
            slug: NormalizeSlug(slug),
            currencyCode: NormalizeCurrency(currencyCode),
            isActive: true,
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    /// <summary>
    /// Rehydrates a shop from its persisted state. No invariants are re-run:
    /// the row was already valid when it was written.
    /// </summary>
    public static Shop Restore(
        Guid id,
        Guid tenantId,
        string name,
        string slug,
        string currencyCode,
        bool isActive,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new Shop(id, tenantId, name, slug, currencyCode, isActive, createdAt, updatedAt);
    }

    public void Rename(string name, DateTime utcNow)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name);
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }

    private static string NormalizeSlug(string slug)
    {
        return Guard.AgainstNullOrWhiteSpace(slug).ToLowerInvariant();
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
