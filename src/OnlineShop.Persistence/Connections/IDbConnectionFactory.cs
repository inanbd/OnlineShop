using System.Data;

namespace OnlineShop.Persistence.Connections;

/// <summary>
/// Creates database connections. There is deliberately no single generic
/// "CreateConnection" method: the caller must say whether it is reading or
/// writing, and that choice is visible at every call site.
/// </summary>
/// <remarks>
/// <code>
/// Query   -> MediatR query handler   -> CreateReadConnection()  -> Dapper -> read replica
/// Command -> MediatR command handler -> CreateWriteConnection() -> Dapper -> primary
/// </code>
/// <para>
/// Connections are returned unopened. The caller owns the lifetime and is
/// expected to dispose them.
/// </para>
/// </remarks>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Opens against <c>ReadConnection</c>, which may point at an
    /// asynchronously replicated read replica.
    /// </summary>
    IDbConnection CreateReadConnection();

    /// <summary>
    /// Opens against <c>WriteConnection</c>, which always points at the primary.
    /// All transactions use this connection.
    /// </summary>
    IDbConnection CreateWriteConnection();
}
