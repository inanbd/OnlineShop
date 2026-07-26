using System.Runtime.CompilerServices;

namespace OnlineShop.Domain.Common;

/// <summary>
/// Small argument-validation helpers used by aggregate factory methods.
/// </summary>
public static class Guard
{
    public static Guid AgainstEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException($"'{name}' must not be an empty GUID.");
        }

        return value;
    }

    public static string AgainstNullOrWhiteSpace(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"'{name}' must not be null or whitespace.");
        }

        return value.Trim();
    }

    public static decimal AgainstNegative(decimal value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0m)
        {
            throw new DomainException($"'{name}' must not be negative.");
        }

        return value;
    }

    public static int AgainstNotPositive(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= 0)
        {
            throw new DomainException($"'{name}' must be greater than zero.");
        }

        return value;
    }
}
