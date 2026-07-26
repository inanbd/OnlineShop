using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Ordering;

/// <summary>
/// A single ordered line. SKU, name and unit price are copied at order time so
/// the order stays readable after the catalog changes.
/// </summary>
public sealed class OrderItem : ITenantOwned
{
    private OrderItem(
        Guid id,
        Guid tenantId,
        Guid orderId,
        Guid productId,
        string sku,
        string productName,
        int quantity,
        decimal unitPrice)
    {
        Id = id;
        TenantId = tenantId;
        OrderId = orderId;
        ProductId = productId;
        Sku = sku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid OrderId { get; }

    public Guid ProductId { get; }

    public string Sku { get; }

    public string ProductName { get; }

    public int Quantity { get; }

    public decimal UnitPrice { get; }

    public decimal LineTotal => UnitPrice * Quantity;

    public static OrderItem Create(
        Guid tenantId,
        Guid orderId,
        Guid productId,
        string sku,
        string productName,
        int quantity,
        decimal unitPrice)
    {
        return new OrderItem(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            orderId: Guard.AgainstEmpty(orderId),
            productId: Guard.AgainstEmpty(productId),
            sku: Guard.AgainstNullOrWhiteSpace(sku),
            productName: Guard.AgainstNullOrWhiteSpace(productName),
            quantity: Guard.AgainstNotPositive(quantity),
            unitPrice: Guard.AgainstNegative(unitPrice));
    }

    public static OrderItem Restore(
        Guid id,
        Guid tenantId,
        Guid orderId,
        Guid productId,
        string sku,
        string productName,
        int quantity,
        decimal unitPrice)
    {
        return new OrderItem(id, tenantId, orderId, productId, sku, productName, quantity, unitPrice);
    }
}
