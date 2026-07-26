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
}
