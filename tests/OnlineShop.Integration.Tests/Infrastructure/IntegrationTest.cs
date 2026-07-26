namespace OnlineShop.Integration.Tests.Infrastructure;

/// <summary>
/// Base for tests that need a real SQL Server. Empties both databases before
/// each test, and reports itself as skipped when no server is available rather
/// than failing.
/// </summary>
[Collection(DatabaseCollection.Name)]
public abstract class IntegrationTest : IAsyncLifetime
{
    protected IntegrationTest(SqlServerFixture fixture)
    {
        Fixture = fixture;
    }

    protected SqlServerFixture Fixture { get; }

    protected static string Primary => SqlServerFixture.PrimaryDatabase;

    protected static string Replica => SqlServerFixture.ReplicaDatabase;

    public async Task InitializeAsync()
    {
        if (Fixture.IsAvailable)
        {
            await Fixture.ResetAsync();
        }
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Call at the top of every test.</summary>
    protected void RequireDatabase() =>
        Skip.IfNot(Fixture.IsAvailable, Fixture.UnavailableReason ?? "SQL Server is unavailable.");
}
