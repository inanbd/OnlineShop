using System.Data;
using OnlineShop.Application.Abstractions.Persistence;

namespace OnlineShop.Persistence.Connections;

/// <summary>
/// Decorates <see cref="SqlConnectionFactory"/> and refuses connections that
/// would break the CQRS database rule.
/// </summary>
/// <remarks>
/// <para>The rule:</para>
/// <code>
/// Queries  -> ReadConnection
/// Commands -> WriteConnection
/// </code>
/// <para>
/// A comment cannot stop a query handler from calling
/// <c>CreateWriteConnection()</c> and quietly pushing storefront traffic onto
/// the primary. This class can, and it fails loudly at the call site instead of
/// six months later under load.
/// </para>
/// <para>Two things are rejected:</para>
/// <list type="bullet">
/// <item>
/// a query asking for the write connection without a documented consistency
/// requirement — that is, without <see cref="StrongConsistencyAllowedAttribute"/>
/// on the query type and <see cref="ReadConsistency.Strong"/> on the request;
/// </item>
/// <item>
/// a command asking for the read connection, which would put a write, or a
/// read a write depends on, on a possibly stale replica.
/// </item>
/// </list>
/// <para>
/// Outside a MediatR request — startup, migrations, background jobs — the kind
/// is <see cref="DbOperationKind.Unspecified"/> and nothing is blocked.
/// </para>
/// </remarks>
internal sealed class CqrsGuardedDbConnectionFactory : IDbConnectionFactory
{
    private readonly IDbConnectionFactory _inner;
    private readonly IDbOperationContext _operationContext;

    public CqrsGuardedDbConnectionFactory(
        IDbConnectionFactory inner,
        IDbOperationContext operationContext)
    {
        _inner = inner;
        _operationContext = operationContext;
    }

    public IDbConnection CreateReadConnection()
    {
        if (_operationContext.CurrentKind == DbOperationKind.Command)
        {
            throw new CqrsConnectionViolationException(
                "A command handler asked for ReadConnection. Commands run against WriteConnection so that " +
                "they write to the primary and read their own uncommitted work. Basing a write on a replica " +
                "read risks acting on data that is already stale. Use IUnitOfWork, which opens the write " +
                "connection and hands every repository the same transaction.");
        }

        return _inner.CreateReadConnection();
    }

    public IDbConnection CreateWriteConnection()
    {
        if (_operationContext.CurrentKind == DbOperationKind.Query
            && _operationContext.StrongConsistencyJustification is null)
        {
            throw new CqrsConnectionViolationException(
                "A query handler asked for WriteConnection without a documented consistency requirement. " +
                "Queries read from ReadConnection so that reporting and catalog traffic stay off the primary. " +
                "If this read genuinely cannot tolerate replication lag, mark the query type with " +
                "[StrongConsistencyAllowed(\"reason\")] and pass ReadConsistency.Strong.");
        }

        return _inner.CreateWriteConnection();
    }
}
