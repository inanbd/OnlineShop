namespace OnlineShop.Persistence.Repositories;

/// <summary>
/// Thrown when a write affected no rows, meaning the row it targeted no longer
/// matches the conditions the caller assumed.
/// </summary>
/// <remarks>
/// Because every write is filtered by <c>TenantId</c>, this also covers an
/// attempt to modify another tenant's row: the statement simply matches nothing
/// and reports zero rows affected rather than silently doing nothing.
/// </remarks>
public sealed class DbConcurrencyException : Exception
{
    public DbConcurrencyException(string message)
        : base(message)
    {
    }
}
