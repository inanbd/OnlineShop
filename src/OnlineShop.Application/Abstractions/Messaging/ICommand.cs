using MediatR;

namespace OnlineShop.Application.Abstractions.Messaging;

/// <summary>
/// Marker for a state-changing request. Commands always run against the write
/// connection, inside a transaction.
/// </summary>
public interface ICommand<TResponse> : IRequest<TResponse>
{
}

/// <summary>
/// Marker for a state-changing request that returns no value.
/// </summary>
public interface ICommand : IRequest
{
}
