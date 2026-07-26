using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Ordering;

public enum PaymentStatus
{
    Authorized = 0,
    Captured = 1,
    Failed = 2,
    Refunded = 3,
}

/// <summary>
/// The payment record written alongside an order in the same write transaction.
/// </summary>
public sealed class Payment : ITenantOwned
{
    private Payment(
        Guid id,
        Guid tenantId,
        Guid orderId,
        string provider,
        string providerReference,
        decimal amount,
        string currencyCode,
        PaymentStatus status,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        OrderId = orderId;
        Provider = provider;
        ProviderReference = providerReference;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid OrderId { get; }

    public string Provider { get; }

    public string ProviderReference { get; }

    public decimal Amount { get; }

    public string CurrencyCode { get; }

    public PaymentStatus Status { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public static Payment Record(
        Guid tenantId,
        Guid orderId,
        string provider,
        string providerReference,
        decimal amount,
        string currencyCode,
        PaymentStatus status,
        DateTime utcNow)
    {
        return new Payment(
            id: Guid.NewGuid(),
            tenantId: Guard.AgainstEmpty(tenantId),
            orderId: Guard.AgainstEmpty(orderId),
            provider: Guard.AgainstNullOrWhiteSpace(provider),
            providerReference: Guard.AgainstNullOrWhiteSpace(providerReference),
            amount: Guard.AgainstNegative(amount),
            currencyCode: Guard.AgainstNullOrWhiteSpace(currencyCode).ToUpperInvariant(),
            status: status,
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    public static Payment Restore(
        Guid id,
        Guid tenantId,
        Guid orderId,
        string provider,
        string providerReference,
        decimal amount,
        string currencyCode,
        PaymentStatus status,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new Payment(
            id,
            tenantId,
            orderId,
            provider,
            providerReference,
            amount,
            currencyCode,
            status,
            createdAt,
            updatedAt);
    }

    public void MarkRefunded(DateTime utcNow)
    {
        if (Status is not (PaymentStatus.Authorized or PaymentStatus.Captured))
        {
            throw new DomainException($"A payment in status {Status} cannot be refunded.");
        }

        Status = PaymentStatus.Refunded;
        UpdatedAt = utcNow;
    }
}
