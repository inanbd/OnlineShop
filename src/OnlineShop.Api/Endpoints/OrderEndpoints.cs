using MediatR;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Application.Orders.Commands.CancelOrder;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Application.Orders.Queries.GetOrderDetails;
using OnlineShop.Application.Orders.Queries.GetOrders;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        // Query -> ReadConnection -> replica.
        group.MapGet("/", async (
            ISender sender,
            Guid? shopId,
            Guid? customerId,
            OrderStatus? status,
            DateTime? placedFrom,
            DateTime? placedTo,
            string? orderNumber,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default) =>
        {
            var filter = new OrderFilter
            {
                ShopId = shopId,
                CustomerId = customerId,
                Status = status,
                PlacedFromUtc = placedFrom,
                PlacedToUtc = placedTo,
                OrderNumber = orderNumber,
                Page = page,
                PageSize = pageSize,
            };

            var result = await sender.Send(new GetOrdersQuery(filter), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetOrders")
        .WithSummary("Lists orders. Served from the read replica.");

        group.MapGet("/{orderId:guid}", async (
            ISender sender,
            Guid orderId,
            ReadConsistency consistency = ReadConsistency.Eventual,
            CancellationToken cancellationToken = default) =>
        {
            var order = await sender.Send(new GetOrderDetailsQuery(orderId, consistency), cancellationToken);
            return Results.Ok(order);
        })
        .WithName("GetOrderDetails")
        .WithSummary("Gets one order. Checkout passes consistency=Strong for the confirmation page.");

        // Command -> WriteConnection -> primary. Order, items, inventory,
        // payment, cart and status history all commit or all roll back.
        group.MapPost("/", async (
            ISender sender,
            PlaceOrderCommand command,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);

            // The confirmation read must hit the primary: this order is
            // milliseconds old.
            return Results.Created(
                $"/api/orders/{result.OrderId}?consistency={nameof(ReadConsistency.Strong)}",
                result);
        })
        .WithName("PlaceOrder")
        .WithSummary("Places an order in one write transaction.");

        group.MapPost("/{orderId:guid}/cancel", async (
            ISender sender,
            Guid orderId,
            CancelOrderRequest request,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new CancelOrderCommand(orderId, request.Reason, request.CancelledByUserId),
                cancellationToken);

            return Results.NoContent();
        })
        .WithName("CancelOrder")
        .WithSummary("Cancels an order and releases its stock reservations.");

        return app;
    }

    public sealed record CancelOrderRequest(string Reason, Guid? CancelledByUserId);
}
