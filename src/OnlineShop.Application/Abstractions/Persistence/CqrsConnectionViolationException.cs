namespace OnlineShop.Application.Abstractions.Persistence;

/// <summary>
/// Thrown when code asks for a connection that the CQRS database rule forbids
/// for the operation currently in flight.
/// </summary>
/// <remarks>
/// This is a programming error, not a runtime condition: it means a query
/// handler reached for the write database without a documented consistency
/// requirement, or a command handler reached for the read database.
/// </remarks>
public sealed class CqrsConnectionViolationException : InvalidOperationException
{
    public CqrsConnectionViolationException(string message)
        : base(message)
    {
    }
}
