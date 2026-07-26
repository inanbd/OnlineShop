using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Ordering;

public enum CartStatus
{
    Open = 0,
    CheckedOut = 1,
    Abandoned = 2,
}

/// <summary>
/// A customer's in-progress basket. Checkout converts it into an order and
/// closes the cart inside the same write transaction.
/// </summary>
public sealed class Cart : ITenantOwned
{
    private readonly List<CartItem> _items;

    private Cart(
        Guid id,
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        CartStatus status,
        List<CartItem> items,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        ShopId = shopId;
        CustomerId = customerId;
        Status = status;
        _items = items;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ShopId { get; }

    public Guid CustomerId { get; }

    public CartStatus Status { get; private set; }

    public IReadOnlyList<CartItem> Items => _items;

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public static Cart Create(Guid tenantId, Guid shopId, Guid customerId, DateTime utcNow)
    {
        return new Cart(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            shopId: Guard.AgainstEmpty(shopId),
            customerId: Guard.AgainstEmpty(customerId),
            status: CartStatus.Open,
            items: [],
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    public static Cart Restore(
        Guid id,
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        CartStatus status,
        IEnumerable<CartItem> items,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new Cart(id, tenantId, shopId, customerId, status, [.. items], createdAt, updatedAt);
    }

    public void MarkCheckedOut(DateTime utcNow)
    {
        if (Status != CartStatus.Open)
        {
            throw new DomainException($"Cart '{Id}' is not open (current status: {Status}).");
        }

        if (_items.Count == 0)
        {
            throw new DomainException($"Cart '{Id}' is empty and cannot be checked out.");
        }

        Status = CartStatus.CheckedOut;
        UpdatedAt = utcNow;
    }
}

public sealed class CartItem
{
    private CartItem(
        Guid id,
        Guid tenantId,
        Guid cartId,
        Guid productId,
        int quantity,
        decimal unitPrice)
    {
        Id = id;
        TenantId = tenantId;
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid CartId { get; }

    public Guid ProductId { get; }

    public int Quantity { get; }

    public decimal UnitPrice { get; }

    public decimal LineTotal => UnitPrice * Quantity;

    public static CartItem Restore(
        Guid id,
        Guid tenantId,
        Guid cartId,
        Guid productId,
        int quantity,
        decimal unitPrice)
    {
        return new CartItem(id, tenantId, cartId, productId, quantity, unitPrice);
    }
}
