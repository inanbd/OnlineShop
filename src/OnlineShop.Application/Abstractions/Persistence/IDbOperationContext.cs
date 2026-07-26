namespace OnlineShop.Application.Abstractions.Persistence;

/// <summary>
/// What kind of MediatR request is currently executing.
/// </summary>
public enum DbOperationKind
{
    /// <summary>No CQRS scope is active (startup, background job, integration test setup).</summary>
    Unspecified = 0,

    /// <summary>An <c>IQuery&lt;T&gt;</c> is executing. Reads only, from <c>ReadConnection</c>.</summary>
    Query = 1,

    /// <summary>An <c>ICommand&lt;T&gt;</c> is executing. Writes, from <c>WriteConnection</c>.</summary>
    Command = 2,
}

/// <summary>
/// Ambient record of the operation currently in flight, used by the connection
/// factory to refuse a connection that would break the CQRS database rule.
/// </summary>
/// <remarks>
/// The scope is flow-local: it follows <c>await</c> continuations and does not
/// leak between concurrent requests.
/// </remarks>
public interface IDbOperationContext
{
    DbOperationKind CurrentKind { get; }

    /// <summary>
    /// Non-null when the active query has been explicitly cleared to read from
    /// the write database. Carries the documented reason.
    /// </summary>
    string? StrongConsistencyJustification { get; }

    /// <summary>
    /// Opens an operation scope. Disposing restores the previous scope.
    /// </summary>
    IDisposable BeginScope(DbOperationKind kind, string? strongConsistencyJustification = null);
}
