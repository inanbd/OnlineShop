using System.Data;
using Microsoft.Data.SqlClient;

namespace OnlineShop.Persistence.Connections;

/// <summary>
/// Creates SQL Server connections from the two configured connection strings.
/// </summary>
/// <remarks>
/// <code>
/// ReadConnection  -> read replica (or the primary, in environments without one)
/// WriteConnection -> primary
/// </code>
/// <para>
/// This type only maps a name to a connection string. The rule about which of
/// the two a given handler is allowed to ask for is enforced by
/// <see cref="CqrsGuardedDbConnectionFactory"/>, which decorates this one.
/// </para>
/// </remarks>
public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _readConnectionString;
    private readonly string _writeConnectionString;

    public SqlConnectionFactory(
        string readConnectionString,
        string writeConnectionString)
    {
        if (string.IsNullOrWhiteSpace(readConnectionString))
        {
            throw new ArgumentException(
                "The 'ReadConnection' connection string is missing. Queries have no database to read from.",
                nameof(readConnectionString));
        }

        if (string.IsNullOrWhiteSpace(writeConnectionString))
        {
            throw new ArgumentException(
                "The 'WriteConnection' connection string is missing. Commands have no database to write to.",
                nameof(writeConnectionString));
        }

        _readConnectionString = readConnectionString;
        _writeConnectionString = writeConnectionString;
    }

    public IDbConnection CreateReadConnection()
    {
        return new SqlConnection(_readConnectionString);
    }

    public IDbConnection CreateWriteConnection()
    {
        return new SqlConnection(_writeConnectionString);
    }
}
