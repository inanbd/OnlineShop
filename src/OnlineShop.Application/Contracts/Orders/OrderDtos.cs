using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Contracts.Orders;

/// <summary>Row projection for order listing screens.</summary>
public sealed class OrderListItemDto
{
    public Guid Id { get; init; }

    public Guid ShopId { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string CustomerEmail { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public decimal GrandTotal { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public int ItemCount { get; init; }

    public DateTime PlacedAt { get; init; }
}

/// <summary>Full order projection returned by <c>GetOrderDetailsQuery</c>.</summary>
public sealed class OrderDetailsDto
{
    public Guid Id { get; init; }

    public Guid ShopId { get; init; }

    public string ShopName { get; init; } = string.Empty;

    public string OrderNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string CustomerEmail { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public decimal Subtotal { get; init; }

    public decimal TaxTotal { get; init; }

    public decimal GrandTotal { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public DateTime PlacedAt { get; init; }

    public DateTime? CancelledAt { get; init; }

    public string? CancellationReason { get; init; }

    public IReadOnlyList<OrderItemDto> Items { get; init; } = [];

    public IReadOnlyList<PaymentDto> Payments { get; init; } = [];

    public IReadOnlyList<OrderStatusHistoryDto> StatusHistory { get; init; } = [];
}

public sealed class OrderItemDto
{
    public Guid Id { get; init; }

    public Guid ProductId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }
}

public sealed class PaymentDto
{
    public Guid Id { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string ProviderReference { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public PaymentStatus Status { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class OrderStatusHistoryDto
{
    public Guid Id { get; init; }

    public OrderStatus? FromStatus { get; init; }

    public OrderStatus ToStatus { get; init; }

    public string? Reason { get; init; }

    public DateTime OccurredAt { get; init; }
}

/// <summary>Filter for order listing. Every field is applied as a parameter.</summary>
public sealed record OrderFilter
{
    public Guid? ShopId { get; init; }

    public Guid? CustomerId { get; init; }

    public OrderStatus? Status { get; init; }

    public DateTime? PlacedFromUtc { get; init; }

    public DateTime? PlacedToUtc { get; init; }

    /// <summary>Matches order number, exact or prefix.</summary>
    public string? OrderNumber { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
