using System.Data;
using System.Data.Common;

namespace OnlineShop.Persistence.Internal;

internal static class DbExtensions
{
    /// <summary>
    /// Opens the connection asynchronously if it is not already open.
    /// </summary>
    /// <remarks>
    /// Dapper opens a closed connection for you, but synchronously, which
    /// blocks a thread pool thread for the duration of the handshake. Opening
    /// here keeps the call path genuinely asynchronous.
    /// </remarks>
    public static async Task EnsureOpenAsync(this IDbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
        {
            return;
        }

        if (connection is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            connection.Open();
        }
    }

    /// <summary>
    /// Returns the connection the transaction is running on.
    /// </summary>
    /// <remarks>
    /// This is how repositories obtain a connection: they take the ambient
    /// transaction and use its connection, never the factory. That is what
    /// guarantees no repository opens a second write connection in the middle
    /// of someone else's transaction.
    /// </remarks>
    public static IDbConnection RequireConnection(this IDbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no connection. It has already been committed, rolled back or disposed, " +
                "so no further repository call can take part in it.");
    }
}
