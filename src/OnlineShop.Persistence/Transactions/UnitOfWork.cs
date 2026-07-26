using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Internal;

namespace OnlineShop.Persistence.Transactions;

/// <summary>
/// Runs a command's work inside one transaction on the write connection.
/// </summary>
/// <remarks>
/// <para>
/// This is the only type in the solution that opens a write connection and
/// begins a transaction. Repositories receive the resulting
/// <see cref="IDbTransaction"/> and use its connection, so a command that
/// touches five tables still uses exactly one connection and one transaction.
/// </para>
/// <code>
/// WriteConnection
/// BEGIN TRANSACTION
///   ... operation(transaction) ...
/// COMMIT              (ROLLBACK if the operation throws)
/// </code>
/// </remarks>
internal sealed class UnitOfWork : IUnitOfWork
{
    /// <summary>
    /// SQL Server's default. Individual commands raise it when they need to,
    /// as order placement does to keep concurrent checkouts from overselling.
    /// </summary>
    private const IsolationLevel DefaultIsolationLevel = IsolationLevel.ReadCommitted;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<UnitOfWork> _logger;

    public UnitOfWork(IDbConnectionFactory connectionFactory, ILogger<UnitOfWork> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<IDbTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(DefaultIsolationLevel, operation, cancellationToken);
    }

    public async Task ExecuteAsync(
        Func<IDbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteAsync(
            DefaultIsolationLevel,
            async (transaction, ct) =>
            {
                await operation(transaction, ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        IsolationLevel isolationLevel,
        Func<IDbTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Rule 6: transactions always use the write connection.
        var connection = _connectionFactory.CreateWriteConnection();

        try
        {
            await connection.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

            var transaction = await BeginTransactionAsync(connection, isolationLevel, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var result = await operation(transaction, cancellationToken).ConfigureAwait(false);

                await CommitAsync(transaction).ConfigureAwait(false);

                return result;
            }
            catch
            {
                await SafeRollbackAsync(transaction).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await DisposeAsync(transaction).ConfigureAwait(false);
            }
        }
        finally
        {
            await DisposeAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task<IDbTransaction> BeginTransactionAsync(
        IDbConnection connection,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        if (connection is DbConnection dbConnection)
        {
            return await dbConnection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }

        return connection.BeginTransaction(isolationLevel);
    }

    /// <remarks>
    /// The commit deliberately does not take the request's cancellation token.
    /// Once the work has succeeded, aborting mid-commit would leave the outcome
    /// genuinely unknown — the server may have committed anyway — and the
    /// rollback that followed could not put that right. Cancellation is honoured
    /// up to this point; from here the transaction is seen through.
    /// </remarks>
    private static async Task CommitAsync(IDbTransaction transaction)
    {
        if (transaction is DbTransaction dbTransaction)
        {
            await dbTransaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            transaction.Commit();
        }
    }

    /// <remarks>
    /// A rollback failure must never replace the exception that caused it: the
    /// original error is what explains the failure, and this one is usually just
    /// "the connection is already gone". It is logged and swallowed so the real
    /// exception propagates.
    /// </remarks>
    private async Task SafeRollbackAsync(IDbTransaction transaction)
    {
        try
        {
            if (transaction is DbTransaction dbTransaction)
            {
                await dbTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                transaction.Rollback();
            }
        }
        catch (Exception rollbackFailure)
        {
            _logger.LogError(
                rollbackFailure,
                "Rolling back the write transaction failed. The original failure is being rethrown.");
        }
    }

    private static async Task DisposeAsync(object resource)
    {
        switch (resource)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
