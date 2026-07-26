namespace OnlineShop.Application.Abstractions;

/// <summary>
/// Thrown when a tenant-scoped lookup finds nothing.
/// </summary>
/// <remarks>
/// A row belonging to another tenant is indistinguishable from a row that does
/// not exist, because every lookup is filtered by <c>TenantId</c>. That is
/// deliberate: it keeps a caller from probing for other tenants' identifiers.
/// </remarks>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string entityName, Guid id)
        : base($"{entityName} '{id}' was not found.")
    {
        EntityName = entityName;
        Id = id;
    }

    public string EntityName { get; }

    public Guid Id { get; }
}
