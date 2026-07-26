using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OnlineShop.Application;
using OnlineShop.Application.Abstractions;
using OnlineShop.Persistence;

namespace OnlineShop.Integration.Tests.Infrastructure;

/// <summary>
/// Wires the real application and persistence registrations against the test
/// databases.
/// </summary>
/// <remarks>
/// Requests go through the genuine path — MediatR, the CQRS scope behavior, the
/// guarded connection factory, Dapper, SQL Server — so these tests exercise the
/// wiring as configured, not a stand-in for it.
/// </remarks>
public sealed class TestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TestHarness(ServiceProvider services, TestTenantContext tenantContext, TestClock clock)
    {
        _services = services;
        TenantContext = tenantContext;
        Clock = clock;
    }

    public TestTenantContext TenantContext { get; }

    public TestClock Clock { get; }

    /// <summary>
    /// The production topology: queries read the replica, commands write the
    /// primary.
    /// </summary>
    public static TestHarness Split(SqlServerFixture fixture, Guid tenantId) =>
        Create(fixture.ReplicaConnectionString, fixture.PrimaryConnectionString, tenantId);

    /// <summary>
    /// Both names point at the primary, as in a development environment with a
    /// single instance. Used to exercise read-model SQL without having to
    /// replicate first.
    /// </summary>
    public static TestHarness SinglePrimary(SqlServerFixture fixture, Guid tenantId) =>
        Create(fixture.PrimaryConnectionString, fixture.PrimaryConnectionString, tenantId);

    private static TestHarness Create(string readConnection, string writeConnection, Guid tenantId)
    {
        var tenantContext = new TestTenantContext { TenantId = tenantId };
        var clock = new TestClock();

        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddPersistence(options =>
        {
            options.ReadConnection = readConnection;
            options.WriteConnection = writeConnection;
            options.EnforceCqrsConnectionRule = true;
        });

        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<IDateTimeProvider>(clock);

        // The Dapper-backed Identity stores, exercised through the same wiring
        // the host uses.
        services.AddDapperIdentityStores();

        return new TestHarness(services.BuildServiceProvider(), tenantContext, clock);
    }

    /// <summary>
    /// Sends a request in its own DI scope, the way one HTTP request would.
    /// </summary>
    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(request, cancellationToken);
    }

    public async Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(request, cancellationToken);
    }

    public T GetRequiredService<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>
    /// Runs work in its own DI scope, for tests that drive persistence directly
    /// rather than through a MediatR request.
    /// </summary>
    public async Task<TResult> WithServicesAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = _services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

public sealed class TestTenantContext : ITenantContext
{
    public Guid TenantId { get; set; }

    public bool HasTenant => TenantId != Guid.Empty;
}

public sealed class TestClock : IDateTimeProvider
{
    private DateTime _utcNow = new(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

    public DateTime UtcNow => _utcNow;

    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);

    public void Set(DateTime utcNow) => _utcNow = utcNow;
}
