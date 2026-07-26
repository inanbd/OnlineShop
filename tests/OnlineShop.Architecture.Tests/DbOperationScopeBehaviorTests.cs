using MediatR;
using OnlineShop.Application.Abstractions.Behaviors;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Persistence.Connections;

namespace OnlineShop.Architecture.Tests;

/// <summary>
/// The pipeline behavior is what classifies a request as a query or a command.
/// Without it the guard has nothing to enforce against, so these tests cover
/// the classification itself.
/// </summary>
public sealed class DbOperationScopeBehaviorTests
{
    private readonly DbOperationContext _operationContext = new();

    [Fact]
    public async Task A_query_opens_a_query_scope()
    {
        var observed = await RunAsync(new SampleQuery());

        Assert.Equal(DbOperationKind.Query, observed.Kind);
        Assert.Null(observed.Justification);
    }

    [Fact]
    public async Task A_command_opens_a_command_scope()
    {
        var observed = await RunAsync(new SampleCommand());

        Assert.Equal(DbOperationKind.Command, observed.Kind);
    }

    [Fact]
    public async Task A_query_defaulting_to_eventual_consistency_gets_no_allowance()
    {
        var observed = await RunAsync(new DocumentedStrongQuery(ReadConsistency.Eventual));

        Assert.Equal(DbOperationKind.Query, observed.Kind);
        Assert.Null(observed.Justification);
    }

    [Fact]
    public async Task A_documented_query_asking_for_strong_consistency_carries_its_reason()
    {
        var observed = await RunAsync(new DocumentedStrongQuery(ReadConsistency.Strong));

        Assert.Equal(DbOperationKind.Query, observed.Kind);
        Assert.Equal(DocumentedStrongQuery.Reason, observed.Justification);
    }

    [Fact]
    public async Task An_undocumented_query_asking_for_strong_consistency_is_rejected()
    {
        // This is the case the rule exists for: without the attribute, a
        // developer could quietly move a hot storefront read onto the primary.
        var violation = await Assert.ThrowsAsync<CqrsConnectionViolationException>(
            () => RunAsync(new UndocumentedStrongQuery(ReadConsistency.Strong)));

        // The message has to name the attribute the way it is written in source,
        // so it points at the fix.
        Assert.Contains("[StrongConsistencyAllowed]", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_is_neither_opens_no_scope()
    {
        var observed = await RunAsync(new PlainRequest());

        Assert.Equal(DbOperationKind.Unspecified, observed.Kind);
    }

    [Fact]
    public async Task The_scope_closes_even_when_the_handler_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(new SampleCommand(), _ => throw new InvalidOperationException("handler failed")));

        Assert.Equal(DbOperationKind.Unspecified, _operationContext.CurrentKind);
    }

    private Task<ObservedScope> RunAsync<TRequest>(TRequest request)
        where TRequest : notnull
    {
        return RunAsync(request, _ => Task.CompletedTask);
    }

    /// <summary>
    /// Runs the behavior around a stand-in handler that reports what the
    /// ambient scope looked like while it executed.
    /// </summary>
    private async Task<ObservedScope> RunAsync<TRequest>(
        TRequest request,
        Func<TRequest, Task> handler)
        where TRequest : notnull
    {
        var behavior = new DbOperationScopeBehavior<TRequest, ObservedScope>(_operationContext);

        return await behavior.Handle(
            request,
            async () =>
            {
                await handler(request);
                return new ObservedScope(
                    _operationContext.CurrentKind,
                    _operationContext.StrongConsistencyJustification);
            },
            CancellationToken.None);
    }

    private sealed record ObservedScope(DbOperationKind Kind, string? Justification);

    private sealed record SampleQuery : IQuery<ObservedScope>;

    private sealed record SampleCommand : ICommand<ObservedScope>;

    private sealed record PlainRequest : IRequest<ObservedScope>;

    [StrongConsistencyAllowed(Reason)]
    private sealed record DocumentedStrongQuery(ReadConsistency Consistency)
        : IQuery<ObservedScope>, ISupportsReadConsistency
    {
        public const string Reason = "Reads a row this request just wrote.";
    }

    private sealed record UndocumentedStrongQuery(ReadConsistency Consistency)
        : IQuery<ObservedScope>, ISupportsReadConsistency;
}
