namespace OnlineShop.Application.Abstractions.Persistence;

/// <summary>
/// Implemented by queries whose caller may choose the consistency level.
/// </summary>
/// <remarks>
/// Requesting <see cref="ReadConsistency.Strong"/> additionally requires
/// <see cref="StrongConsistencyAllowedAttribute"/> on the query type.
/// </remarks>
public interface ISupportsReadConsistency
{
    ReadConsistency Consistency { get; }
}
