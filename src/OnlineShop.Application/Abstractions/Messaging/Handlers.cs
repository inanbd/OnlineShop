using MediatR;

namespace OnlineShop.Application.Abstractions.Messaging;

/// <summary>
/// Handler for a read-only request. Runs against <c>ReadConnection</c>.
/// </summary>
public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
}

/// <summary>
/// Handler for a state-changing request. Runs against <c>WriteConnection</c>,
/// inside a transaction.
/// </summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
}

/// <summary>
/// Handler for a state-changing request that returns no value.
/// </summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand
{
}
