using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Ordering;

public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Fulfilled = 2,
    Cancelled = 3,
}

/// <summary>
/// A placed order together with its line items.
/// </summary>
/// <remarks>
/// Order, order items, inventory reservation, payment record, cart closure and
/// status history are all written under a single <see cref="System.Data.IDbTransaction"/>
/// on the write connection. See <c>PlaceOrderCommandHandler</c>.
/// </remarks>
public sealed class Order : ITenantOwned
{
    private readonly List<OrderItem> _items;

    private Order(
        Guid id,
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        string orderNumber,
        OrderStatus status,
        string currencyCode,
        List<OrderItem> items,
        DateTime placedAt,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? cancelledAt,
        string? cancellationReason)
    {
        Id = id;
        TenantId = tenantId;
        ShopId = shopId;
        CustomerId = customerId;
        OrderNumber = orderNumber;
        Status = status;
        CurrencyCode = currencyCode;
        _items = items;
        PlacedAt = placedAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CancelledAt = cancelledAt;
        CancellationReason = cancellationReason;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ShopId { get; }

    public Guid CustomerId { get; }

    public string OrderNumber { get; }

    public OrderStatus Status { get; private set; }

    public string CurrencyCode { get; }

    public IReadOnlyList<OrderItem> Items => _items;

    public DateTime PlacedAt { get; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public decimal Subtotal => _items.Sum(item => item.LineTotal);

    public decimal TaxTotal => decimal.Round(Subtotal * TaxRate, 4, MidpointRounding.AwayFromZero);

    public decimal GrandTotal => Subtotal + TaxTotal;

    /// <summary>
    /// Flat placeholder rate. A real deployment resolves this per shop and
    /// destination; it is kept simple here because tax rules are not what this
    /// codebase is demonstrating.
    /// </summary>
    private const decimal TaxRate = 0.00m;

    public static Order Place(
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        string orderNumber,
        string currencyCode,
        IEnumerable<OrderLine> lines,
        DateTime utcNow)
    {
        var orderId = Guid.NewGuid();

        var items = lines
            .Select(line => OrderItem.Create(
                tenantId: tenantId,
                orderId: orderId,
                productId: line.ProductId,
                sku: line.Sku,
                productName: line.ProductName,
                quantity: line.Quantity,
                unitPrice: line.UnitPrice))
            .ToList();

        if (items.Count == 0)
        {
            throw new DomainException("An order must contain at least one line item.");
        }

        return new Order(
            id: orderId,
            tenantId: Guard.AgainstEmpty(tenantId),
            shopId: Guard.AgainstEmpty(shopId),
            customerId: Guard.AgainstEmpty(customerId),
            orderNumber: Guard.AgainstNullOrWhiteSpace(orderNumber),
            status: OrderStatus.Pending,
            currencyCode: Guard.AgainstNullOrWhiteSpace(currencyCode).ToUpperInvariant(),
            items: items,
            placedAt: utcNow,
            createdAt: utcNow,
            updatedAt: utcNow,
            cancelledAt: null,
            cancellationReason: null);
    }

    public static Order Restore(
        Guid id,
        Guid tenantId,
        Guid shopId,
        Guid customerId,
        string orderNumber,
        OrderStatus status,
        string currencyCode,
        IEnumerable<OrderItem> items,
        DateTime placedAt,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? cancelledAt,
        string? cancellationReason)
    {
        return new Order(
            id,
            tenantId,
            shopId,
            customerId,
            orderNumber,
            status,
            currencyCode,
            [.. items],
            placedAt,
            createdAt,
            updatedAt,
            cancelledAt,
            cancellationReason);
    }

    public void MarkPaid(DateTime utcNow)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"Order '{OrderNumber}' cannot be paid from status {Status}.");
        }

        Status = OrderStatus.Paid;
        UpdatedAt = utcNow;
    }

    public void Cancel(string reason, DateTime utcNow)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainException($"Order '{OrderNumber}' is already cancelled.");
        }

        if (Status == OrderStatus.Fulfilled)
        {
            throw new DomainException($"Order '{OrderNumber}' has been fulfilled and can no longer be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = Guard.AgainstNullOrWhiteSpace(reason);
        CancelledAt = utcNow;
        UpdatedAt = utcNow;
    }
}

/// <summary>
/// Input for a single line when placing an order.
/// </summary>
public sealed record OrderLine(
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
