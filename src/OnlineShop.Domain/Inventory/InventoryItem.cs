using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Inventory;

/// <summary>
/// Stock position for a single product in a single shop.
/// </summary>
/// <remarks>
/// The domain type expresses the reservation rules. The authoritative
/// oversell check happens inside the write transaction as a conditional
/// UPDATE, so that concurrent order placement cannot both pass a
/// read-then-write check. See <c>InventoryRepository.TryReserveAsync</c>.
/// </remarks>
public sealed class InventoryItem : ITenantOwned
{
    private InventoryItem(
        Guid id,
        Guid tenantId,
        Guid shopId,
        Guid productId,
        int quantityOnHand,
        int quantityReserved,
        int reorderThreshold,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        ShopId = shopId;
        ProductId = productId;
        QuantityOnHand = quantityOnHand;
        QuantityReserved = quantityReserved;
        ReorderThreshold = reorderThreshold;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ShopId { get; }

    public Guid ProductId { get; }

    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }

    public int ReorderThreshold { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    public bool NeedsReorder => QuantityAvailable <= ReorderThreshold;

    public static InventoryItem Create(
        Guid tenantId,
        Guid shopId,
        Guid productId,
        int quantityOnHand,
        int reorderThreshold,
        DateTime utcNow)
    {
        if (quantityOnHand < 0)
        {
            throw new DomainException("Quantity on hand must not be negative.");
        }

        if (reorderThreshold < 0)
        {
            throw new DomainException("Reorder threshold must not be negative.");
        }

        return new InventoryItem(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            shopId: Guard.AgainstEmpty(shopId),
            productId: Guard.AgainstEmpty(productId),
            quantityOnHand: quantityOnHand,
            quantityReserved: 0,
            reorderThreshold: reorderThreshold,
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    public static InventoryItem Restore(
        Guid id,
        Guid tenantId,
        Guid shopId,
        Guid productId,
        int quantityOnHand,
        int quantityReserved,
        int reorderThreshold,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new InventoryItem(
            id,
            tenantId,
            shopId,
            productId,
            quantityOnHand,
            quantityReserved,
            reorderThreshold,
            createdAt,
            updatedAt);
    }

    public void Reserve(int quantity, DateTime utcNow)
    {
        Guard.AgainstNotPositive(quantity);

        if (quantity > QuantityAvailable)
        {
            throw new DomainException(
                $"Cannot reserve {quantity} unit(s) of product '{ProductId}': only {QuantityAvailable} available.");
        }

        QuantityReserved += quantity;
        UpdatedAt = utcNow;
    }

    public void ReleaseReservation(int quantity, DateTime utcNow)
    {
        Guard.AgainstNotPositive(quantity);

        if (quantity > QuantityReserved)
        {
            throw new DomainException(
                $"Cannot release {quantity} unit(s) of product '{ProductId}': only {QuantityReserved} reserved.");
        }

        QuantityReserved -= quantity;
        UpdatedAt = utcNow;
    }
}
