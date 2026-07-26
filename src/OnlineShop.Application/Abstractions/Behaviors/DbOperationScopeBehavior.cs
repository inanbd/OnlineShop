using System.Reflection;
using MediatR;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;

namespace OnlineShop.Application.Abstractions.Behaviors;

/// <summary>
/// Classifies each request as a query or a command and publishes that to
/// <see cref="IDbOperationContext"/> for the duration of the handler.
/// </summary>
/// <remarks>
/// <para>
/// This is what turns the CQRS database rule from a convention into something
/// the process actually enforces. With the scope in place,
/// <c>CqrsGuardedDbConnectionFactory</c> can reject a query handler that
/// reaches for <c>CreateWriteConnection()</c> or a command handler that reaches
/// for <c>CreateReadConnection()</c>.
/// </para>
/// <para>
/// The one sanctioned exception is a query that both implements
/// <see cref="ISupportsReadConsistency"/> with
/// <see cref="ReadConsistency.Strong"/> and carries
/// <see cref="StrongConsistencyAllowedAttribute"/>. Asking for Strong without
/// the attribute fails here rather than silently sending storefront traffic to
/// the primary.
/// </para>
/// </remarks>
public sealed class DbOperationScopeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IDbOperationContext _operationContext;

    public DbOperationScopeBehavior(IDbOperationContext operationContext)
    {
        _operationContext = operationContext;
    }

    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var kind = Classify(request);

        if (kind == DbOperationKind.Unspecified)
        {
            return next();
        }

        var justification = kind == DbOperationKind.Query
            ? ResolveStrongConsistencyJustification(request)
            : null;

        return HandleInScopeAsync(kind, justification, next);
    }

    private async Task<TResponse> HandleInScopeAsync(
        DbOperationKind kind,
        string? justification,
        RequestHandlerDelegate<TResponse> next)
    {
        using (_operationContext.BeginScope(kind, justification))
        {
            return await next().ConfigureAwait(false);
        }
    }

    private static DbOperationKind Classify(TRequest request)
    {
        var requestType = request.GetType();

        var isCommand = requestType.GetInterfaces().Any(IsCommandInterface);
        var isQuery = requestType.GetInterfaces().Any(IsQueryInterface);

        if (isCommand && isQuery)
        {
            throw new InvalidOperationException(
                $"'{requestType.Name}' implements both ICommand and IQuery. A request must be one or the other, " +
                "because that is what decides whether it runs on the write connection or the read connection.");
        }

        if (isCommand)
        {
            return DbOperationKind.Command;
        }

        return isQuery ? DbOperationKind.Query : DbOperationKind.Unspecified;
    }

    private static bool IsCommandInterface(Type type)
    {
        if (type == typeof(ICommand))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommand<>);
    }

    private static bool IsQueryInterface(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQuery<>);
    }

    private static string? ResolveStrongConsistencyJustification(TRequest request)
    {
        if (request is not ISupportsReadConsistency { Consistency: ReadConsistency.Strong })
        {
            return null;
        }

        var requestType = request.GetType();
        var allowance = requestType.GetCustomAttribute<StrongConsistencyAllowedAttribute>(inherit: false);

        if (allowance is null)
        {
            throw new CqrsConnectionViolationException(
                $"Query '{requestType.Name}' asked for ReadConsistency.Strong, which routes the read to " +
                "WriteConnection, but the query type is not marked with [StrongConsistencyAllowed]. " +
                "Queries read from ReadConnection unless there is a documented consistency requirement: " +
                "either drop back to ReadConsistency.Eventual, or annotate the query with the reason it " +
                "cannot tolerate replication lag.");
        }

        return allowance.Justification;
    }
}
