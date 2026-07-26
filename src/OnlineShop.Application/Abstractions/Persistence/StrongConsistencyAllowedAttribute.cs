namespace OnlineShop.Application.Abstractions.Persistence;

/// <summary>
/// Documents why a query is permitted to read from the write database.
/// </summary>
/// <remarks>
/// The architectural rule is: <c>Queries -> ReadConnection</c>. The single
/// exception the rule allows is a *documented* consistency requirement, and
/// this attribute is that documentation. Without it, a query that asks for
/// <see cref="ReadConsistency.Strong"/> is rejected at run time rather than
/// quietly sending storefront traffic to the primary.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class StrongConsistencyAllowedAttribute : Attribute
{
    public StrongConsistencyAllowedAttribute(string justification)
    {
        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new ArgumentException(
                "A strong-consistency allowance must state why the read cannot tolerate replication lag.",
                nameof(justification));
        }

        Justification = justification;
    }

    public string Justification { get; }
}
