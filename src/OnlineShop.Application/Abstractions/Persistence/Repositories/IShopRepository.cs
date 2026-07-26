using System.Data;
using OnlineShop.Domain.Shops;

namespace OnlineShop.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Write side of shops and shop membership. Implemented by <c>ShopRepository</c>.
/// </summary>
public interface IShopRepository
{
    Task<Shop?> GetByIdAsync(
        Guid tenantId,
        Guid shopId,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(
        Guid tenantId,
        string slug,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        Shop shop,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Shop shop,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<ShopMember?> GetMemberByEmailAsync(
        Guid tenantId,
        Guid shopId,
        string email,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task InsertMemberAsync(
        ShopMember member,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task UpdateMemberAsync(
        ShopMember member,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}
