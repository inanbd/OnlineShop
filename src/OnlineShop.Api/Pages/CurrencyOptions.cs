using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Api.Pages;

/// <summary>
/// The currencies a shop can be opened in.
/// </summary>
/// <remarks>
/// One list drives both the dropdown and the validation, so the two cannot
/// drift apart.
/// </remarks>
public static class CurrencyOptions
{
    public static readonly IReadOnlyList<string> Supported = ["USD", "EUR", "GBP"];

    public const string Default = "USD";

    public static bool IsSupported(string? code) =>
        code is not null && Supported.Contains(code, StringComparer.Ordinal);
}

/// <summary>
/// Validates a currency against <see cref="CurrencyOptions.Supported"/>.
/// </summary>
/// <remarks>
/// Deliberately not <c>[StringLength(3, MinimumLength = 3)]</c>. That renders
/// <c>data-val-length</c>, and jQuery Validation measures a
/// <c>&lt;select&gt;</c> by its number of selected options rather than by the
/// length of its value — so a perfectly valid "USD" counts as length 1 and the
/// form can never be submitted with JavaScript enabled. This attribute emits no
/// client-side length rule and checks membership on the server instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SupportedCurrencyAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is string code && CurrencyOptions.IsSupported(code);

    public override string FormatErrorMessage(string name) =>
        $"Choose one of: {string.Join(", ", CurrencyOptions.Supported)}.";
}
