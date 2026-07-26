using OnlineShop.Application.Contracts.Customers;

namespace OnlineShop.Application.Abstractions.Persistence.Queries;

/// <summary>
/// Read model for customers. Implemented by <c>CustomerQueries</c> over
/// <c>ReadConnection</c>.
/// </summary>
public interface ICustomerQueries
{
    Task<IReadOnlyList<CustomerListItemDto>> GetAsync(
        Guid tenantId,
        CustomerFilter filter,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        Guid tenantId,
        CustomerFilter filter,
        CancellationToken cancellationToken = default);
}
