namespace OnlineShop.Application.Abstractions.Persistence;

/// <summary>
/// Chooses which database a read is served from.
/// </summary>
/// <remarks>
/// <para>
/// <c>ReadConnection</c> may point at an asynchronously replicated read
/// replica, so a row written a moment ago is not guaranteed to be visible
/// there yet:
/// </para>
/// <code>
/// Create Product -> Write Database -> (replication lag) -> Read Replica
/// </code>
/// <para>Routing:</para>
/// <code>
/// Eventual -> ReadConnection   (default; storefront, catalog, reporting)
/// Strong   -> WriteConnection  (read-after-write; use only when necessary)
/// </code>
/// <para>
/// A query may only ask for <see cref="Strong"/> if its request type carries
/// <see cref="StrongConsistencyAllowedAttribute"/> documenting why. That is
/// enforced at run time by <c>DbOperationScopeBehavior</c>.
/// </para>
/// </remarks>
public enum ReadConsistency
{
    /// <summary>Serve from <c>ReadConnection</c>. Replication lag is acceptable.</summary>
    Eventual = 0,

    /// <summary>Serve from <c>WriteConnection</c> because the caller must observe its own recent write.</summary>
    Strong = 1,
}
