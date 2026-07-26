using System.Data;
using System.Reflection;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Persistence.Connections;

namespace OnlineShop.Architecture.Tests;

/// <summary>
/// The structural rules: no Entity Framework, no database concerns in the
/// domain or the endpoints, and no repository opening its own connection.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(Domain.Catalog.Product).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IQuery<>).Assembly;
    private static readonly Assembly PersistenceAssembly = typeof(IDbConnectionFactory).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    public static TheoryData<string> AllAssemblies() =>
    [
        DomainAssembly.GetName().Name!,
        ApplicationAssembly.GetName().Name!,
        PersistenceAssembly.GetName().Name!,
        ApiAssembly.GetName().Name!,
    ];

    [Theory]
    [MemberData(nameof(AllAssemblies))]
    public void No_assembly_references_entity_framework(string assemblyName)
    {
        var assembly = Assemblies().Single(a => a.GetName().Name == assemblyName);

        var entityFrameworkReferences = assembly
            .GetReferencedAssemblies()
            .Where(reference => reference.Name is not null
                                && reference.Name.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.Name!)
            .ToList();

        Assert.True(
            entityFrameworkReferences.Count == 0,
            $"{assemblyName} references {string.Join(", ", entityFrameworkReferences)}. " +
            "This solution uses Dapper and ADO.NET; Entity Framework is not part of it.");
    }

    [Fact]
    public void The_domain_references_no_data_access_package()
    {
        // Rule 13: database concerns live in persistence, never in the domain.
        var dataAccessReferences = DomainAssembly
            .GetReferencedAssemblies()
            .Where(reference => reference.Name is not null && IsDataAccess(reference.Name))
            .Select(reference => reference.Name!)
            .ToList();

        Assert.True(
            dataAccessReferences.Count == 0,
            $"OnlineShop.Domain references {string.Join(", ", dataAccessReferences)}. The domain must not know " +
            "how, or whether, its state is persisted.");
    }

    [Fact]
    public void Endpoints_do_not_manage_connections_or_transactions()
    {
        // Rule 13: connection management never reaches the HTTP layer.
        var offenders = ApiAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("OnlineShop.Api.Endpoints", StringComparison.Ordinal) == true)
            .Where(type => MemberTypes(type).Any(IsConnectionConcern))
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These endpoint types touch a connection or transaction: {string.Join(", ", offenders)}. " +
            "Endpoints send a MediatR request; the database is the persistence layer's business.");
    }

    [Fact]
    public void Razor_pages_do_not_manage_connections_or_transactions()
    {
        // Rule 13 again, now that there is a UI. A page model sends a MediatR
        // request and shapes the response; if one held an IDbConnection or began
        // a transaction, the read/write routing would stop being decided by
        // whether the request is a query or a command.
        var offenders = ApiAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("OnlineShop.Api.Pages", StringComparison.Ordinal) == true)
            .Where(type => MemberTypes(type).Any(IsConnectionConcern))
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These page models touch a connection or transaction: {string.Join(", ", offenders)}. " +
            "Pages send MediatR requests; the database is the persistence layer's business.");
    }

    [Fact]
    public void The_ui_actually_has_page_models_to_check()
    {
        // Keeps the rule above from passing vacuously if the namespace moves.
        var pageModels = ApiAssembly
            .GetTypes()
            .Count(type => type.Namespace?.StartsWith("OnlineShop.Api.Pages", StringComparison.Ordinal) == true
                           && type.Name.EndsWith("Model", StringComparison.Ordinal));

        Assert.True(pageModels > 10, $"Only {pageModels} page models were discovered.");
    }

    [Fact]
    public void Repositories_never_hold_a_connection_factory()
    {
        // Rule 11: a repository must use the transaction it is given, never open
        // a connection of its own. Having no factory to reach for is what makes
        // that structural rather than a matter of discipline.
        var offenders = PersistenceAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("OnlineShop.Persistence.Repositories", StringComparison.Ordinal) == true)
            .Where(type => type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(field => field.FieldType == typeof(IDbConnectionFactory)))
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These repositories hold an IDbConnectionFactory: {string.Join(", ", offenders)}. A repository takes " +
            "part in the caller's transaction by using transaction.Connection. Opening a second connection would " +
            "put its work outside that transaction, so a rollback would not undo it.");
    }

    [Fact]
    public void Every_repository_method_takes_the_transaction_it_must_join()
    {
        var repositoryInterfaces = ApplicationAssembly
            .GetTypes()
            .Where(type => type.IsInterface
                           && type.Namespace == "OnlineShop.Application.Abstractions.Persistence.Repositories");

        var offenders = new List<string>();

        foreach (var repositoryInterface in repositoryInterfaces)
        {
            foreach (var method in repositoryInterface.GetMethods())
            {
                var takesTransaction = method
                    .GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(IDbTransaction));

                if (!takesTransaction)
                {
                    offenders.Add($"{repositoryInterface.Name}.{method.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"These write-repository methods do not accept an IDbTransaction: {string.Join(", ", offenders)}. " +
            "Every participant in a command must be handed the same transaction, or its work is not atomic with " +
            "the rest.");
    }

    [Fact]
    public void Read_models_never_take_a_transaction()
    {
        // The read side runs on ReadConnection, outside any transaction. A
        // query interface taking IDbTransaction would mean it is being driven
        // from a command's write connection.
        var queryInterfaces = ApplicationAssembly
            .GetTypes()
            .Where(type => type.IsInterface
                           && type.Namespace == "OnlineShop.Application.Abstractions.Persistence.Queries");

        var offenders = queryInterfaces
            .SelectMany(queryInterface => queryInterface.GetMethods()
                .Where(method => method.GetParameters().Any(p => p.ParameterType == typeof(IDbTransaction)))
                .Select(method => $"{queryInterface.Name}.{method.Name}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These read-model methods accept an IDbTransaction: {string.Join(", ", offenders)}.");
    }

    /// <summary>
    /// The single read model that is deliberately not tenant-scoped.
    /// </summary>
    /// <remarks>
    /// <c>IShopDirectory</c> resolves a public storefront slug to a tenant, so
    /// it is the lookup that <em>establishes</em> the tenant for a request and
    /// cannot itself be filtered by one. It exposes only public shop identity.
    /// </remarks>
    private const string TenantAgnosticReadModel = "IShopDirectory";

    [Fact]
    public void Only_the_storefront_directory_is_tenant_agnostic()
    {
        // Pins the exception, so a second tenant-agnostic read model cannot be
        // introduced without someone editing this test.
        var tenantAgnostic = ApplicationAssembly
            .GetTypes()
            .Where(type => type.IsInterface
                           && type.Namespace == "OnlineShop.Application.Abstractions.Persistence.Queries")
            .Where(type => type.GetMethods().All(method => !method.GetParameters().Any(IsTenantIdParameter)))
            .Select(type => type.Name)
            .ToList();

        Assert.Equal([TenantAgnosticReadModel], tenantAgnostic);
    }

    [Fact]
    public void Every_read_model_method_requires_a_tenant()
    {
        var queryInterfaces = ApplicationAssembly
            .GetTypes()
            .Where(type => type.IsInterface
                           && type.Namespace == "OnlineShop.Application.Abstractions.Persistence.Queries")
            .Where(type => type.Name != TenantAgnosticReadModel);

        var offenders = queryInterfaces
            .SelectMany(queryInterface => queryInterface.GetMethods()
                .Where(method => !method.GetParameters().Any(IsTenantIdParameter))
                .Select(method => $"{queryInterface.Name}.{method.Name}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These read-model methods do not take a tenantId: {string.Join(", ", offenders)}. Tenant scoping is " +
            "passed explicitly so it is visible at the call site rather than hidden in ambient state.");
    }

    private static bool IsTenantIdParameter(ParameterInfo parameter) =>
        parameter.ParameterType == typeof(Guid)
        && string.Equals(parameter.Name, "tenantId", StringComparison.Ordinal);

    private static bool IsDataAccess(string assemblyName) =>
        assemblyName.Contains("Dapper", StringComparison.OrdinalIgnoreCase)
        || assemblyName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase)
        || assemblyName.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase)
        || assemblyName.Equals("System.Data.Common", StringComparison.OrdinalIgnoreCase);

    private static bool IsConnectionConcern(Type type) =>
        type == typeof(IDbConnection)
        || type == typeof(IDbTransaction)
        || type == typeof(IDbConnectionFactory)
        || type == typeof(IUnitOfWork);

    /// <summary>Field, property, parameter and return types declared by a type.</summary>
    private static IEnumerable<Type> MemberTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(flags))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static IEnumerable<Assembly> Assemblies() =>
        [DomainAssembly, ApplicationAssembly, PersistenceAssembly, ApiAssembly];
}
