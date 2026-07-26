using System.Reflection;
using MediatR;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;

namespace OnlineShop.Architecture.Tests;

/// <summary>
/// Checks the shape of the requests themselves, so the pipeline behavior can
/// always tell which database a request belongs on.
/// </summary>
public sealed class CqrsRequestShapeTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IQuery<>).Assembly;

    public static TheoryData<Type> AllRequests()
    {
        var data = new TheoryData<Type>();

        foreach (var type in RequestTypes())
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllRequests))]
    public void Every_request_is_a_query_or_a_command_but_not_both(Type requestType)
    {
        var isQuery = ImplementsOpenInterface(requestType, typeof(IQuery<>));
        var isCommand = ImplementsOpenInterface(requestType, typeof(ICommand<>))
                        || typeof(ICommand).IsAssignableFrom(requestType);

        Assert.True(
            isQuery ^ isCommand,
            $"""
             '{requestType.Name}' is {Describe(isQuery, isCommand)}.

             Every request must be exactly one of the two. That is what decides whether it
             runs on ReadConnection or WriteConnection; a request that is neither would slip
             past the guard entirely.
             """);
    }

    [Theory]
    [MemberData(nameof(AllRequests))]
    public void Only_queries_choose_a_consistency_level(Type requestType)
    {
        if (!typeof(ISupportsReadConsistency).IsAssignableFrom(requestType))
        {
            return;
        }

        Assert.True(
            ImplementsOpenInterface(requestType, typeof(IQuery<>)),
            $"'{requestType.Name}' implements ISupportsReadConsistency but is not a query. Commands always run " +
            "on the write connection, so there is no consistency level for them to pick.");
    }

    [Theory]
    [MemberData(nameof(AllRequests))]
    public void A_query_that_can_ask_for_strong_consistency_documents_why(Type requestType)
    {
        if (!typeof(ISupportsReadConsistency).IsAssignableFrom(requestType))
        {
            return;
        }

        var allowance = requestType.GetCustomAttribute<StrongConsistencyAllowedAttribute>(inherit: false);

        Assert.True(
            allowance is not null,
            $"""
             '{requestType.Name}' can be asked for ReadConsistency.Strong, which routes its read to
             WriteConnection, but does not say why.

             Mark it with [StrongConsistencyAllowed("reason")] explaining what breaks if the read is
             served from a replica, or drop ISupportsReadConsistency so it always reads the replica.
             """);

        Assert.False(
            string.IsNullOrWhiteSpace(allowance!.Justification),
            $"'{requestType.Name}' has an empty strong-consistency justification.");
    }

    [Fact]
    public void The_expected_handlers_all_exist()
    {
        // The named handlers from the specification, so a rename or a deletion
        // shows up here rather than as a missing route at run time.
        string[] expected =
        [
            "GetProductsQuery",
            "GetProductByIdQuery",
            "GetOrdersQuery",
            "GetOrderDetailsQuery",
            "GetShopDashboardQuery",
            "CreateShopCommand",
            "CreateProductCommand",
            "UpdateProductCommand",
            "DeleteProductCommand",
            "PlaceOrderCommand",
            "CancelOrderCommand",
            "InviteShopMemberCommand",
        ];

        var actual = RequestTypes().Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(name => !actual.Contains(name)).ToList();

        Assert.True(missing.Count == 0, $"Missing requests: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void Every_request_has_exactly_one_handler()
    {
        // Both handler shapes have to be counted. In MediatR 12 the
        // void-returning IRequestHandler<TRequest> is a separate interface, not
        // a specialisation of IRequestHandler<TRequest, Unit>, so looking only
        // at the two-argument form silently misses every command that returns
        // nothing.
        var handledRequests = ApplicationAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(IsRequestHandlerInterface)
            .Select(@interface => @interface.GetGenericArguments()[0])
            .ToList();

        var unhandled = RequestTypes()
            .Where(request => !handledRequests.Contains(request))
            .Select(request => request.Name)
            .ToList();

        Assert.True(unhandled.Count == 0, $"Requests with no handler: {string.Join(", ", unhandled)}.");

        var duplicated = handledRequests
            .GroupBy(request => request)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Name)
            .ToList();

        Assert.True(duplicated.Count == 0, $"Requests with more than one handler: {string.Join(", ", duplicated)}.");
    }

    private static string Describe(bool isQuery, bool isCommand) => (isQuery, isCommand) switch
    {
        (true, true) => "both a query and a command",
        (false, false) => "neither a query nor a command",
        _ => "correctly classified",
    };

    private static bool IsRequestHandlerInterface(Type @interface)
    {
        if (!@interface.IsGenericType)
        {
            return false;
        }

        var definition = @interface.GetGenericTypeDefinition();

        return definition == typeof(IRequestHandler<,>) || definition == typeof(IRequestHandler<>);
    }

    private static bool ImplementsOpenInterface(Type type, Type openInterface) =>
        type.GetInterfaces().Any(@interface => @interface.IsGenericType
                                               && @interface.GetGenericTypeDefinition() == openInterface);

    private static IEnumerable<Type> RequestTypes() =>
        ApplicationAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(IBaseRequest).IsAssignableFrom(type));
}
