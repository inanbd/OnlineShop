namespace OnlineShop.Application.Contracts.Carts;

/// <summary>A shopper's basket, joined to live catalog and stock figures.</summary>
public sealed class CartDto
{
    public Guid Id { get; init; }

    public Guid ShopId { get; init; }

    public string ShopName { get; init; } = string.Empty;

    public string ShopSlug { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public IReadOnlyList<CartLineDto> Lines { get; init; } = [];

    public decimal Subtotal => Lines.Sum(line => line.LineTotal);

    public int TotalQuantity => Lines.Sum(line => line.Quantity);

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>True when any line asks for more than the shop can currently supply.</summary>
    public bool HasUnavailableLines => Lines.Any(line => !line.IsAvailable);
}

public sealed class CartLineDto
{
    public Guid ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public int Quantity { get; init; }

    /// <summary>The price held on the cart line.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Today's catalog price, which may have moved since the line was added.</summary>
    public decimal CurrentPrice { get; init; }

    public decimal LineTotal => UnitPrice * Quantity;

    public int QuantityAvailable { get; init; }

    public bool IsActive { get; init; }

    public bool IsAvailable => IsActive && QuantityAvailable >= Quantity;

    /// <summary>
    /// True when the catalog price has moved since this line was added. Checkout
    /// re-reads the price from the primary, so the shopper is told beforehand.
    /// </summary>
    public bool PriceChanged => CurrentPrice != UnitPrice;
}
