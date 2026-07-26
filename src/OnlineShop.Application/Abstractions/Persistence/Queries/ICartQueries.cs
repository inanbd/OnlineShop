using OnlineShop.Application.Contracts.Carts;

namespace OnlineShop.Application.Abstractions.Persistence.Queries;

/// <summary>
/// Read model for the basket. Implemented by <c>CartQueries</c>.
/// </summary>
/// <remarks>
/// The cart is the clearest everyday case of read-after-write in a storefront:
/// a shopper adds an item and is immediately shown the basket. That read cannot
/// be served from a replica that has not seen the write yet, or the item they
/// just added appears to have vanished — so the page that follows an add,
/// update or removal asks for <see cref="ReadConsistency.Strong"/>, while
/// simply viewing the basket does not.
/// </remarks>
public interface ICartQueries
{
    Task<CartDto?> GetOpenCartAsync(
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default);
}
