using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Customers;

/// <summary>
/// A shopper registered against a shop within a tenant.
/// </summary>
public sealed class Customer : ITenantOwned
{
    private Customer(
        Guid id,
        Guid tenantId,
        Guid shopId,
        string email,
        string fullName,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        ShopId = shopId;
        Email = email;
        FullName = fullName;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ShopId { get; }

    public string Email { get; private set; }

    public string FullName { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public static Customer Create(
        Guid tenantId,
        Guid shopId,
        string email,
        string fullName,
        DateTime utcNow)
    {
        return new Customer(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            shopId: Guard.AgainstEmpty(shopId),
            email: Guard.AgainstNullOrWhiteSpace(email).ToLowerInvariant(),
            fullName: Guard.AgainstNullOrWhiteSpace(fullName),
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    public static Customer Restore(
        Guid id,
        Guid tenantId,
        Guid shopId,
        string email,
        string fullName,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new Customer(id, tenantId, shopId, email, fullName, createdAt, updatedAt);
    }

    public void UpdateProfile(string fullName, DateTime utcNow)
    {
        FullName = Guard.AgainstNullOrWhiteSpace(fullName);
        UpdatedAt = utcNow;
    }
}
