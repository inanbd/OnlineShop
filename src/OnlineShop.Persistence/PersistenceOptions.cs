namespace OnlineShop.Persistence;

/// <summary>
/// The two connection strings, plus the switch that turns the CQRS guard on.
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>Configuration key for <see cref="ReadConnection"/>.</summary>
    public const string ReadConnectionName = "ReadConnection";

    /// <summary>Configuration key for <see cref="WriteConnection"/>.</summary>
    public const string WriteConnectionName = "WriteConnection";

    /// <summary>
    /// Serves all queries. May point at an asynchronously replicated read
    /// replica.
    /// </summary>
    public string ReadConnection { get; set; } = string.Empty;

    /// <summary>
    /// Serves all commands and all transactions. Always the primary.
    /// </summary>
    public string WriteConnection { get; set; } = string.Empty;

    /// <summary>
    /// When true (the default), a query handler asking for the write connection
    /// without a documented consistency requirement — or a command handler
    /// asking for the read connection — throws.
    /// </summary>
    /// <remarks>
    /// Turning this off removes the only mechanical protection the rule has.
    /// It exists for hosts that run outside the MediatR pipeline entirely, such
    /// as a migration runner, not as a way to quiet a failing handler.
    /// </remarks>
    public bool EnforceCqrsConnectionRule { get; set; } = true;

    /// <summary>
    /// True when both connection strings point at the same server and database,
    /// which is the normal single-instance development setup.
    /// </summary>
    /// <remarks>
    /// Worth knowing at startup: with one database, a read-after-write bug is
    /// invisible locally and only appears once a real replica is introduced.
    /// </remarks>
    public bool IsUsingSingleDatabase =>
        string.Equals(ReadConnection, WriteConnection, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ReadConnection))
        {
            throw new InvalidOperationException(
                $"Connection string '{ReadConnectionName}' is not configured. Every query needs it.");
        }

        if (string.IsNullOrWhiteSpace(WriteConnection))
        {
            throw new InvalidOperationException(
                $"Connection string '{WriteConnectionName}' is not configured. Every command needs it.");
        }
    }
}
