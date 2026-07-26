using MediatR;
using OnlineShop.Application.Dashboard.Queries.GetShopDashboard;
using OnlineShop.Application.Shops.Commands.CreateShop;
using OnlineShop.Application.Shops.Commands.InviteShopMember;

namespace OnlineShop.Api.Endpoints;

public static class ShopEndpoints
{
    public static IEndpointRouteBuilder MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shops").WithTags("Shops");

        // Command -> WriteConnection -> primary.
        group.MapPost("/", async (
            ISender sender,
            CreateShopCommand command,
            CancellationToken cancellationToken) =>
        {
            var shopId = await sender.Send(command, cancellationToken);
            return Results.Created($"/api/shops/{shopId}", new { id = shopId });
        })
        .WithName("CreateShop")
        .WithSummary("Creates a shop and its owner membership in one transaction.");

        group.MapPost("/{shopId:guid}/members", async (
            ISender sender,
            Guid shopId,
            InviteShopMemberRequest request,
            CancellationToken cancellationToken) =>
        {
            var memberId = await sender.Send(
                new InviteShopMemberCommand(shopId, request.Email, request.Role, request.InvitedByUserId),
                cancellationToken);

            return Results.Created($"/api/shops/{shopId}/members/{memberId}", new { id = memberId });
        })
        .WithName("InviteShopMember")
        .WithSummary("Invites a user to a shop.");

        // Query -> ReadConnection. Analytics never touches the primary.
        group.MapGet("/{shopId:guid}/dashboard", async (
            ISender sender,
            Guid shopId,
            int windowDays = 30,
            CancellationToken cancellationToken = default) =>
        {
            var dashboard = await sender.Send(new GetShopDashboardQuery(shopId, windowDays), cancellationToken);
            return Results.Ok(dashboard);
        })
        .WithName("GetShopDashboard")
        .WithSummary("Shop analytics. Always served from the read replica.");

        return app;
    }

    public sealed record InviteShopMemberRequest(
        string Email,
        Domain.Shops.ShopMemberRole Role,
        Guid InvitedByUserId);
}
