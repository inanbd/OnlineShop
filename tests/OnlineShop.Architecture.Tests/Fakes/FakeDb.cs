using System.Data;

namespace OnlineShop.Architecture.Tests.Fakes;

/// <summary>
/// Minimal ADO.NET fakes. They record what happened rather than talking to a
/// server, which is enough to verify connection choice and transaction
/// lifecycle without a SQL Server instance.
/// </summary>
public sealed class FakeDbConnection : IDbConnection
{
    public FakeDbConnection(string role)
    {
        Role = role;
    }

    /// <summary>"Read" or "Write" — which connection string produced this.</summary>
    public string Role { get; }

    public bool WasOpened { get; private set; }

    public bool WasDisposed { get; private set; }

    public FakeDbTransaction? Transaction { get; private set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public string ConnectionString { get; set; } = string.Empty;

    public int ConnectionTimeout => 15;

    public string Database => "OnlineShop";

    public ConnectionState State { get; private set; } = ConnectionState.Closed;

    public void Open()
    {
        WasOpened = true;
        State = ConnectionState.Open;
    }

    public void Close() => State = ConnectionState.Closed;

    public IDbTransaction BeginTransaction() => BeginTransaction(IsolationLevel.ReadCommitted);

    public IDbTransaction BeginTransaction(IsolationLevel il)
    {
        Transaction = new FakeDbTransaction(this, il);
        return Transaction;
    }

    public IDbCommand CreateCommand() => throw new NotSupportedException("No statement is executed in these tests.");

    public void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public void Dispose()
    {
        WasDisposed = true;
        State = ConnectionState.Closed;
    }
}

public sealed class FakeDbTransaction : IDbTransaction
{
    private FakeDbConnection? _connection;

    public FakeDbTransaction(FakeDbConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    public bool WasCommitted { get; private set; }

    public bool WasRolledBack { get; private set; }

    public bool WasDisposed { get; private set; }

    /// <summary>
    /// Null once the transaction is finished, matching <c>SqlTransaction</c>.
    /// That is what lets <c>RequireConnection</c> detect a repository call made
    /// with a transaction that has already been committed or rolled back.
    /// </summary>
    public IDbConnection? Connection => _connection;

    public IsolationLevel IsolationLevel { get; }

    public void Commit() => WasCommitted = true;

    public void Rollback() => WasRolledBack = true;

    public void Dispose()
    {
        WasDisposed = true;
        _connection = null;
    }
}

/// <summary>
/// Records which of the two connections each call asked for.
/// </summary>
public sealed class RecordingDbConnectionFactory : Persistence.Connections.IDbConnectionFactory
{
    private readonly List<FakeDbConnection> _created = [];

    public IReadOnlyList<FakeDbConnection> Created => _created;

    public int ReadConnectionCount { get; private set; }

    public int WriteConnectionCount { get; private set; }

    public IDbConnection CreateReadConnection()
    {
        ReadConnectionCount++;
        var connection = new FakeDbConnection("Read");
        _created.Add(connection);
        return connection;
    }

    public IDbConnection CreateWriteConnection()
    {
        WriteConnectionCount++;
        var connection = new FakeDbConnection("Write");
        _created.Add(connection);
        return connection;
    }
}
