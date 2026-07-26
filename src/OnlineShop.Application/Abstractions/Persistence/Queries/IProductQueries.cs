using OnlineShop.Application.Contracts.Products;

namespace OnlineShop.Application.Abstractions.Persistence.Queries;

/// <summary>
/// Read model for the catalog. Implemented by <c>ProductQueries</c> over
/// <c>ReadConnection</c>.
/// </summary>
/// <remarks>
/// Read models return DTOs, never domain aggregates: the read side projects
/// exactly the columns a screen needs and is free to join across tables that
/// the write side keeps separate.
/// </remarks>
public interface IProductQueries
{
    /// <param name="consistency">
    /// <see cref="ReadConsistency.Eventual"/> (the default) reads the replica.
    /// <see cref="ReadConsistency.Strong"/> reads the primary and is only
    /// permitted for queries carrying <see cref="StrongConsistencyAllowedAttribute"/>.
    /// </param>
    Task<ProductDto?> GetByIdAsync(
        Guid tenantId,
        Guid productId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductListItemDto>> GetAsync(
        Guid tenantId,
        ProductFilter filter,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        Guid tenantId,
        ProductFilter filter,
        CancellationToken cancellationToken = default);
}
