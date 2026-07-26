using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OnlineShop.Application.Abstractions.Identity;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Persistence.Connections;
using OnlineShop.Persistence.Identity;
using OnlineShop.Persistence.Queries;
using OnlineShop.Persistence.Repositories;
using OnlineShop.Persistence.Transactions;

namespace OnlineShop.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the read/write connection factory, the read models and the
    /// write repositories.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        Action<PersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PersistenceOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IDbOperationContext, DbOperationContext>();

        services.AddSingleton<IDbConnectionFactory>(serviceProvider =>
        {
            IDbConnectionFactory factory = new SqlConnectionFactory(
                readConnectionString: options.ReadConnection,
                writeConnectionString: options.WriteConnection);

            if (!options.EnforceCqrsConnectionRule)
            {
                return factory;
            }

            // The guard wraps the real factory, so nothing can reach the
            // unguarded one: it is never registered in the container.
            return new CqrsGuardedDbConnectionFactory(
                factory,
                serviceProvider.GetRequiredService<IDbOperationContext>());
        });

        // The only type that opens a write connection and begins a transaction.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Read models -> ReadConnection.
        services.AddScoped<IProductQueries, ProductQueries>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<ICustomerQueries, CustomerQueries>();
        services.AddScoped<IDashboardQueries, DashboardQueries>();
        services.AddScoped<ICartQueries, CartQueries>();
        services.AddScoped<IShopQueries, ShopQueries>();

        // Public storefront routing. The one read that is not tenant-scoped,
        // because it is what resolves the tenant. See IShopDirectory.
        services.AddScoped<IShopDirectory, ShopDirectory>();

        // Write repositories -> the caller's transaction. These are stateless
        // and hold no connection, so a single instance is safe.
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<IOrderRepository, OrderRepository>();
        services.AddSingleton<IShopRepository, ShopRepository>();
        services.AddSingleton<ICustomerRepository, CustomerRepository>();
        services.AddSingleton<IInventoryRepository, InventoryRepository>();
        services.AddSingleton<ITenantRepository, TenantRepository>();

        return services;
    }

    /// <summary>
    /// Registers the Dapper-backed ASP.NET Core Identity stores.
    /// </summary>
    /// <remarks>
    /// Call this instead of <c>AddEntityFrameworkStores</c>. Identity is the one
    /// place that reads through the write connection on purpose: authentication
    /// must not be decided from a replica that has yet to see a password change
    /// or a lockout.
    /// </remarks>
    public static IServiceCollection AddDapperIdentityStores(this IServiceCollection services)
    {
        services.AddScoped<IUserStore<AppUser>, DapperUserStore>();
        services.AddScoped<IRoleStore<AppRole>, DapperRoleStore>();

        return services;
    }

    /// <summary>
    /// Warns when both connection strings point at the same database, which
    /// hides read-after-write problems until a real replica appears.
    /// </summary>
    public static void LogConnectionTopology(this ILogger logger, PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsUsingSingleDatabase)
        {
            logger.LogWarning(
                "ReadConnection and WriteConnection point at the same database. That is expected in local " +
                "development, but it means replication lag is zero here, so a query that should have asked for " +
                "ReadConsistency.Strong will look correct locally and fail once a read replica is introduced.");
        }
        else
        {
            logger.LogInformation(
                "Read and write traffic are separated: queries use ReadConnection, commands use WriteConnection.");
        }

        if (!options.EnforceCqrsConnectionRule)
        {
            logger.LogWarning(
                "The CQRS connection guard is disabled. Query handlers can reach the write database and command " +
                "handlers can reach the read database without being stopped.");
        }
    }
}
