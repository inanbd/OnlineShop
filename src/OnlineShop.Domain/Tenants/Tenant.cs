using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Tenants;

/// <summary>
/// The root of an isolated slice of data. Every other aggregate carries this
/// tenant's identifier.
/// </summary>
/// <remarks>
/// A tenant is the one thing that cannot itself be tenant-scoped: it is created
/// during registration, before any tenant is in scope. That makes tenant
/// provisioning the single flow in the application that runs without an
/// ambient <c>TenantId</c>.
/// </remarks>
public sealed class Tenant
{
    private Tenant(Guid id, string name, string slug, DateTime createdAt, DateTime updatedAt)
    {
        Id = id;
        Name = name;
        Slug = slug;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public static Tenant Create(string name, string slug, DateTime utcNow)
    {
        return new Tenant(
            id: Guid.NewGuid(),
            name: Guard.AgainstNullOrWhiteSpace(name),
            slug: Guard.AgainstNullOrWhiteSpace(slug).ToLowerInvariant(),
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    public static Tenant Restore(Guid id, string name, string slug, DateTime createdAt, DateTime updatedAt) =>
        new(id, name, slug, createdAt, updatedAt);

    public void Rename(string name, DateTime utcNow)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name);
        UpdatedAt = utcNow;
    }
}
