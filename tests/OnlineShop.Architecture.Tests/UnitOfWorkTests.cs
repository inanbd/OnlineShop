using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineShop.Architecture.Tests.Fakes;
using OnlineShop.Persistence.Repositories;
using OnlineShop.Persistence.Transactions;

namespace OnlineShop.Architecture.Tests;

/// <summary>
/// Transaction behaviour: always the write connection, commit on success,
/// rollback on failure, one transaction shared by everything in the command.
/// </summary>
public sealed class UnitOfWorkTests
{
    private readonly RecordingDbConnectionFactory _factory = new();
    private readonly UnitOfWork _unitOfWork;

    public UnitOfWorkTests()
    {
        _unitOfWork = new UnitOfWork(_factory, NullLogger<UnitOfWork>.Instance);
    }

    [Fact]
    public async Task A_transaction_always_uses_the_write_connection()
    {
        // Rule 6. There is no code path that begins a transaction on the replica.
        await _unitOfWork.ExecuteAsync((_, _) => Task.CompletedTask);

        Assert.Equal(1, _factory.WriteConnectionCount);
        Assert.Equal(0, _factory.ReadConnectionCount);
    }

    [Fact]
    public async Task Successful_work_is_committed_and_everything_is_disposed()
    {
        var result = await _unitOfWork.ExecuteAsync((_, _) => Task.FromResult(42));

        var connection = Assert.Single(_factory.Created);

        Assert.Equal(42, result);
        Assert.True(connection.WasOpened);
        Assert.True(connection.Transaction!.WasCommitted);
        Assert.False(connection.Transaction.WasRolledBack);
        Assert.True(connection.Transaction.WasDisposed);
        Assert.True(connection.WasDisposed);
    }

    [Fact]
    public async Task A_failure_rolls_back_and_the_original_exception_survives()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _unitOfWork.ExecuteAsync<int>((_, _) => throw new InvalidOperationException("out of stock")));

        var connection = Assert.Single(_factory.Created);

        Assert.Equal("out of stock", failure.Message);
        Assert.True(connection.Transaction!.WasRolledBack);
        Assert.False(connection.Transaction.WasCommitted);
        Assert.True(connection.WasDisposed);
    }

    [Fact]
    public async Task Every_participant_receives_the_same_connection_and_transaction()
    {
        // The rule that makes a multi-table command atomic: order, order items,
        // inventory, payment, cart and status history all go through one
        // transaction on one connection.
        var seenTransactions = new List<IDbTransaction>();
        var seenConnections = new List<IDbConnection>();

        await _unitOfWork.ExecuteAsync((transaction, _) =>
        {
            // Stands in for the six repository calls PlaceOrderCommand makes.
            for (var participant = 0; participant < 6; participant++)
            {
                seenTransactions.Add(transaction);
                seenConnections.Add(transaction.Connection!);
            }

            return Task.CompletedTask;
        });

        Assert.Equal(6, seenTransactions.Count);
        Assert.Single(seenTransactions.Distinct());
        Assert.Single(seenConnections.Distinct());
        Assert.Equal(1, _factory.WriteConnectionCount);
    }

    [Fact]
    public async Task The_requested_isolation_level_is_used()
    {
        await _unitOfWork.ExecuteAsync(
            IsolationLevel.RepeatableRead,
            (_, _) => Task.FromResult(true));

        var connection = Assert.Single(_factory.Created);
        Assert.Equal(IsolationLevel.RepeatableRead, connection.Transaction!.IsolationLevel);
    }

    [Fact]
    public async Task The_default_isolation_level_is_read_committed()
    {
        await _unitOfWork.ExecuteAsync((_, _) => Task.CompletedTask);

        var connection = Assert.Single(_factory.Created);
        Assert.Equal(IsolationLevel.ReadCommitted, connection.Transaction!.IsolationLevel);
    }

    [Fact]
    public async Task A_repository_cannot_join_a_transaction_that_has_finished()
    {
        // Rule 11's failure mode: holding onto the transaction past the unit of
        // work and calling a repository with it. The repository would otherwise
        // have nothing to run on, and silently doing something else — opening a
        // fresh connection — would put that write outside the transaction.
        IDbTransaction? escaped = null;

        await _unitOfWork.ExecuteAsync((transaction, _) =>
        {
            escaped = transaction;
            return Task.CompletedTask;
        });

        var repository = new ProductRepository();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid(), escaped!));

        Assert.Contains("already been committed", failure.Message, StringComparison.Ordinal);
    }
}
