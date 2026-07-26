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

    /// <summary>
    /// Adds a line, or increases the existing line for the same product.
    /// </summary>
    /// <returns>The resulting line, whose quantity is the new absolute total.</returns>
    /// <remarks>
    /// Adding the same product twice must not create two lines: the storefront
    /// shows one row per product, and a second row would let a shopper's basket
    /// disagree with itself about how many they are buying.
    /// </remarks>
    public CartItem AddItem(Guid productId, int quantity, decimal unitPrice, DateTime utcNow)
    {
        EnsureOpen();
        Guard.AgainstNotPositive(quantity);
        Guard.AgainstNegative(unitPrice);

        var existing = _items.SingleOrDefault(item => item.ProductId == productId);

        if (existing is not null)
        {
            existing.ChangeQuantity(existing.Quantity + quantity);
            existing.ChangeUnitPrice(unitPrice);
            UpdatedAt = utcNow;
            return existing;
        }

        var added = CartItem.Create(TenantId, Id, productId, quantity, unitPrice);
        _items.Add(added);
        UpdatedAt = utcNow;
        return added;
    }

    /// <summary>Sets a line to an absolute quantity. Zero removes the line.</summary>
    public void UpdateItemQuantity(Guid productId, int quantity, DateTime utcNow)
    {
        EnsureOpen();

        if (quantity < 0)
        {
            throw new DomainException("A cart quantity cannot be negative.");
        }

        var existing = _items.SingleOrDefault(item => item.ProductId == productId)
            ?? throw new DomainException("That product is not in the cart.");

        if (quantity == 0)
        {
            _items.Remove(existing);
        }
        else
        {
            existing.ChangeQuantity(quantity);
        }

        UpdatedAt = utcNow;
    }

    public void RemoveItem(Guid productId, DateTime utcNow)
    {
        EnsureOpen();

        var existing = _items.SingleOrDefault(item => item.ProductId == productId)
            ?? throw new DomainException("That product is not in the cart.");

        _items.Remove(existing);
        UpdatedAt = utcNow;
    }

    public decimal Subtotal => _items.Sum(item => item.LineTotal);

    public int TotalQuantity => _items.Sum(item => item.Quantity);

    private void EnsureOpen()
    {
        if (Status != CartStatus.Open)
        {
            throw new DomainException($"Cart '{Id}' has already been {Status} and can no longer be changed.");
        }
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

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    public static CartItem Create(
        Guid tenantId,
        Guid cartId,
        Guid productId,
        int quantity,
        decimal unitPrice)
    {
        return new CartItem(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            cartId: Guard.AgainstEmpty(cartId),
            productId: Guard.AgainstEmpty(productId),
            quantity: Guard.AgainstNotPositive(quantity),
            unitPrice: Guard.AgainstNegative(unitPrice));
    }

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

    /// <remarks>
    /// Internal because a line's quantity is the cart's business: going through
    /// <see cref="Cart.UpdateItemQuantity"/> is what keeps the cart's open/closed
    /// rule from being bypassed.
    /// </remarks>
    internal void ChangeQuantity(int quantity) => Quantity = Guard.AgainstNotPositive(quantity);

    /// <remarks>
    /// The catalog price is re-read when a shopper adds to an existing line, so
    /// a basket left open overnight reflects today's price rather than
    /// yesterday's. Checkout re-reads it again from the primary regardless.
    /// </remarks>
    internal void ChangeUnitPrice(decimal unitPrice) => UnitPrice = Guard.AgainstNegative(unitPrice);
}
