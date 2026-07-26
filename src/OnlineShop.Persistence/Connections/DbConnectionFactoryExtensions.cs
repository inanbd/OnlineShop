using System.Data;
using OnlineShop.Application.Abstractions.Persistence;

namespace OnlineShop.Persistence.Connections;

/// <summary>
/// Maps a <see cref="ReadConsistency"/> level onto one of the two connections.
/// </summary>
public static class DbConnectionFactoryExtensions
{
    /// <summary>
    /// <code>
    /// Eventual -> ReadConnection   (replica; the default)
    /// Strong   -> WriteConnection  (primary; read-after-write)
    /// </code>
    /// </summary>
    /// <remarks>
    /// This is the single place the mapping is expressed. Read models call it
    /// instead of branching on the consistency level themselves, so the routing
    /// cannot drift between query classes.
    /// </remarks>
    public static IDbConnection CreateConnection(
        this IDbConnectionFactory factory,
        ReadConsistency consistency)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return consistency switch
        {
            ReadConsistency.Eventual => factory.CreateReadConnection(),
            ReadConsistency.Strong => factory.CreateWriteConnection(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(consistency),
                consistency,
                "Unknown read consistency level."),
        };
    }
}
