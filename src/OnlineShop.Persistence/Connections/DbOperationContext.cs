using OnlineShop.Application.Abstractions.Persistence;

namespace OnlineShop.Persistence.Connections;

/// <summary>
/// Flow-local record of the operation currently in flight.
/// </summary>
/// <remarks>
/// <see cref="AsyncLocal{T}"/> follows <c>await</c> continuations, so the scope
/// opened by the MediatR behavior is still visible several layers down in a
/// query class or repository, without being threaded through every signature.
/// Concurrent requests each get their own value.
/// </remarks>
internal sealed class DbOperationContext : IDbOperationContext
{
    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    public DbOperationKind CurrentKind => CurrentScope.Value?.Kind ?? DbOperationKind.Unspecified;

    public string? StrongConsistencyJustification => CurrentScope.Value?.Justification;

    public IDisposable BeginScope(DbOperationKind kind, string? strongConsistencyJustification = null)
    {
        var scope = new Scope(kind, strongConsistencyJustification, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    private sealed class Scope : IDisposable
    {
        private readonly Scope? _parent;
        private bool _disposed;

        public Scope(DbOperationKind kind, string? justification, Scope? parent)
        {
            Kind = kind;
            Justification = justification;
            _parent = parent;
        }

        public DbOperationKind Kind { get; }

        public string? Justification { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentScope.Value = _parent;
        }
    }
}
