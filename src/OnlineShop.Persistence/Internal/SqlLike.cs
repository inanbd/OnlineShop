namespace OnlineShop.Persistence.Internal;

/// <summary>
/// Builds LIKE patterns from user input.
/// </summary>
/// <remarks>
/// The search term is always a parameter, so this is not about SQL injection.
/// It is about wildcards: a shopper searching for "50%" or "PART_1" would
/// otherwise get <c>%</c> and <c>_</c> interpreted as wildcards and see the
/// wrong results. Every statement using these patterns declares
/// <c>ESCAPE '\'</c>.
/// </remarks>
internal static class SqlLike
{
    public const char EscapeCharacter = '\\';

    /// <summary>Wraps the term in wildcards for a "contains" match.</summary>
    public static string Contains(string term)
    {
        return $"%{Escape(term)}%";
    }

    /// <summary>Appends a wildcard for a "starts with" match.</summary>
    public static string StartsWith(string term)
    {
        return $"{Escape(term)}%";
    }

    private static string Escape(string term)
    {
        return term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }
}
