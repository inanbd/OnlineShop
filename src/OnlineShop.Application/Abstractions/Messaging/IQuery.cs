using MediatR;

namespace OnlineShop.Application.Abstractions.Messaging;

/// <summary>
/// Marker for a read-only request.
/// </summary>
/// <remarks>
/// Every request in the system is either an <see cref="IQuery{TResponse}"/> or an
/// <see cref="ICommand{TResponse}"/>. That distinction is what lets
/// <c>DbOperationScopeBehavior</c> classify the ambient operation and lets the
/// connection factory refuse the wrong connection at run time:
/// <code>
/// Queries  -> ReadConnection
/// Commands -> WriteConnection
/// </code>
/// </remarks>
public interface IQuery<TResponse> : IRequest<TResponse>
{
}
