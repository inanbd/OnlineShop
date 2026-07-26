using System.Data;
using OnlineShop.Domain.Customers;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Write side of customers and their carts. Implemented by
/// <c>CustomerRepository</c>.
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(
        Guid tenantId,
        Guid customerId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        Customer customer,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Customer customer,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the customer's open cart with its items, if any.</summary>
    Task<Cart?> GetOpenCartAsync(
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<Cart?> GetCartByIdAsync(
        Guid tenantId,
        Guid cartId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>Closes a cart once its contents have become an order.</summary>
    Task MarkCartCheckedOutAsync(
        Guid tenantId,
        Guid cartId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an empty cart.</summary>
    Task InsertCartAsync(
        Cart cart,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a cart line to an absolute quantity, inserting it if absent.
    /// </summary>
    /// <remarks>
    /// Absolute rather than a delta, so a retried request cannot quietly double
    /// the basket. The caller works out the new total from the loaded cart.
    /// </remarks>
    Task UpsertCartItemAsync(
        Guid tenantId,
        Guid cartId,
        Guid productId,
        int quantity,
        decimal unitPrice,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task RemoveCartItemAsync(
        Guid tenantId,
        Guid cartId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}
