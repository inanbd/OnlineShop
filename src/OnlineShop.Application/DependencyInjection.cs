using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Abstractions.Behaviors;

namespace OnlineShop.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR handlers and the pipeline behavior that scopes each
    /// request as a query or a command.
    /// </summary>
    /// <remarks>
    /// <see cref="DbOperationScopeBehavior{TRequest,TResponse}"/> is registered
    /// first so that its scope is open before any handler runs. Without it the
    /// connection factory has nothing to enforce against.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);
            configuration.AddOpenBehavior(typeof(DbOperationScopeBehavior<,>));
        });

        return services;
    }
}
