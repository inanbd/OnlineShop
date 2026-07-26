namespace OnlineShop.Application.Abstractions;

/// <summary>
/// UTC clock, injected so command handlers stay testable.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
