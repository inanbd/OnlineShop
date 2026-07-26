using OnlineShop.Application.Abstractions;

namespace OnlineShop.Api.Tenancy;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
