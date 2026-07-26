using System.Data;

namespace OnlineShop.Application.Abstractions.Persistence;

/// <summary>
/// Runs a command's work inside one transaction on the write connection.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place a write connection is opened and a transaction is
/// begun. It replaces hand-rolled
/// <c>using connection / OpenAsync / BeginTransaction / Commit</c> blocks in
/// handlers so that commit-and-rollback can never be forgotten:
/// </para>
/// <code>
/// WriteConnection
/// BEGIN TRANSACTION
///   ... repository calls, all given the same IDbTransaction ...
/// COMMIT           (or ROLLBACK if anything throws)
/// </code>
/// <para>
/// The <see cref="IDbTransaction"/> handed to the callback is passed on to every
/// repository taking part, so they all share one connection and one transaction.
/// Repositories must never open a connection of their own.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<IDbTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        Func<IDbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the work at an explicit isolation level. Use for flows that must not
    /// interleave, such as reserving stock while placing an order.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        IsolationLevel isolationLevel,
        Func<IDbTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
