using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Architecture.Tests.Fakes;
using OnlineShop.Persistence.Connections;

namespace OnlineShop.Architecture.Tests;

/// <summary>
/// The CQRS database rule, exercised end to end through the guard:
/// <code>
/// Queries  -> ReadConnection
/// Commands -> WriteConnection
/// </code>
/// </summary>
public sealed class CqrsConnectionRuleTests
{
    private readonly RecordingDbConnectionFactory _inner = new();
    private readonly DbOperationContext _operationContext = new();
    private readonly CqrsGuardedDbConnectionFactory _factory;

    public CqrsConnectionRuleTests()
    {
        _factory = new CqrsGuardedDbConnectionFactory(_inner, _operationContext);
    }

    [Fact]
    public void Query_gets_the_read_connection()
    {
        using var scope = _operationContext.BeginScope(DbOperationKind.Query);

        using var connection = _factory.CreateReadConnection();

        Assert.Equal(1, _inner.ReadConnectionCount);
        Assert.Equal(0, _inner.WriteConnectionCount);
    }

    [Fact]
    public void Query_is_refused_the_write_connection_without_a_documented_reason()
    {
        using var scope = _operationContext.BeginScope(DbOperationKind.Query);

        var violation = Assert.Throws<CqrsConnectionViolationException>(() => _factory.CreateWriteConnection());

        Assert.Contains("StrongConsistencyAllowed", violation.Message, StringComparison.Ordinal);
        Assert.Equal(0, _inner.WriteConnectionCount);
    }

    [Fact]
    public void Query_may_use_the_write_connection_when_strong_consistency_is_documented()
    {
        using var scope = _operationContext.BeginScope(
            DbOperationKind.Query,
            "Confirmation page reads the order it just created.");

        using var connection = _factory.CreateWriteConnection();

        Assert.Equal(1, _inner.WriteConnectionCount);
    }

    [Fact]
    public void Command_gets_the_write_connection()
    {
        using var scope = _operationContext.BeginScope(DbOperationKind.Command);

        using var connection = _factory.CreateWriteConnection();

        Assert.Equal(1, _inner.WriteConnectionCount);
        Assert.Equal(0, _inner.ReadConnectionCount);
    }

    [Fact]
    public void Command_is_refused_the_read_connection()
    {
        using var scope = _operationContext.BeginScope(DbOperationKind.Command);

        var violation = Assert.Throws<CqrsConnectionViolationException>(() => _factory.CreateReadConnection());

        Assert.Contains("WriteConnection", violation.Message, StringComparison.Ordinal);
        Assert.Equal(0, _inner.ReadConnectionCount);
    }

    [Fact]
    public void Outside_a_request_neither_connection_is_blocked()
    {
        // Migration runners and background jobs run with no CQRS scope open.
        Assert.Equal(DbOperationKind.Unspecified, _operationContext.CurrentKind);

        using var read = _factory.CreateReadConnection();
        using var write = _factory.CreateWriteConnection();

        Assert.Equal(1, _inner.ReadConnectionCount);
        Assert.Equal(1, _inner.WriteConnectionCount);
    }

    [Theory]
    [InlineData(ReadConsistency.Eventual, "Read")]
    [InlineData(ReadConsistency.Strong, "Write")]
    public void Read_consistency_selects_the_connection(ReadConsistency consistency, string expectedRole)
    {
        // Eventual -> ReadConnection, Strong -> WriteConnection.
        using var connection = _inner.CreateConnection(consistency);

        Assert.Equal(expectedRole, Assert.IsType<FakeDbConnection>(connection).Role);
    }

    [Fact]
    public void Disposing_a_scope_restores_the_one_around_it()
    {
        using (_operationContext.BeginScope(DbOperationKind.Command))
        {
            Assert.Equal(DbOperationKind.Command, _operationContext.CurrentKind);

            using (_operationContext.BeginScope(DbOperationKind.Query, "documented"))
            {
                Assert.Equal(DbOperationKind.Query, _operationContext.CurrentKind);
                Assert.Equal("documented", _operationContext.StrongConsistencyJustification);
            }

            Assert.Equal(DbOperationKind.Command, _operationContext.CurrentKind);
            Assert.Null(_operationContext.StrongConsistencyJustification);
        }

        Assert.Equal(DbOperationKind.Unspecified, _operationContext.CurrentKind);
    }

    [Fact]
    public async Task Scopes_do_not_leak_between_concurrent_requests()
    {
        // AsyncLocal keeps each request's scope to its own flow. If it did not,
        // one command in flight would let every concurrent query reach the
        // primary unchecked.
        var queryObserved = DbOperationKind.Unspecified;
        var commandObserved = DbOperationKind.Unspecified;

        var queryFlow = Task.Run(async () =>
        {
            using var scope = _operationContext.BeginScope(DbOperationKind.Query);
            await Task.Delay(20);
            queryObserved = _operationContext.CurrentKind;
        });

        var commandFlow = Task.Run(async () =>
        {
            using var scope = _operationContext.BeginScope(DbOperationKind.Command);
            await Task.Delay(20);
            commandObserved = _operationContext.CurrentKind;
        });

        await Task.WhenAll(queryFlow, commandFlow);

        Assert.Equal(DbOperationKind.Query, queryObserved);
        Assert.Equal(DbOperationKind.Command, commandObserved);
        Assert.Equal(DbOperationKind.Unspecified, _operationContext.CurrentKind);
    }
}
