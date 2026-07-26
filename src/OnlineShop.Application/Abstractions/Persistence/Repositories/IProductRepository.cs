using System.Data;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Write side of the catalog. Implemented by <c>ProductRepository</c>.
/// </summary>
/// <remarks>
/// Every method takes the <see cref="IDbTransaction"/> it must take part in.
/// The repository uses that transaction's own connection and never opens one of
/// its own, which is what keeps a whole command on a single write connection.
/// </remarks>
public interface IProductRepository
{
    /// <summary>
    /// Loads a product for modification. Reads through the transaction, so it
    /// sees the primary and any uncommitted work already done in this command.
    /// </summary>
    Task<Product?> GetByIdAsync(
        Guid tenantId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<bool> SkuExistsAsync(
        Guid tenantId,
        Guid shopId,
        string sku,
        Guid? excludingProductId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        Product product,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Product product,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes the product. Orders reference historical catalog rows, so
    /// the row is retained and filtered out of the catalog instead of removed.
    /// </summary>
    Task DeleteAsync(
        Guid tenantId,
        Guid productId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}
