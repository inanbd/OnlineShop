using MediatR;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Products.Commands.CreateProduct;
using OnlineShop.Application.Products.Commands.DeleteProduct;
using OnlineShop.Application.Products.Commands.UpdateProduct;
using OnlineShop.Application.Products.Queries.GetProductById;
using OnlineShop.Application.Products.Queries.GetProducts;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Api.Endpoints;

/// <summary>
/// Catalog endpoints.
/// </summary>
/// <remarks>
/// Endpoints send a MediatR request and shape the response. They hold no
/// connection, open no transaction and know nothing about which database serves
/// them — that is decided by whether the request is a query or a command.
/// </remarks>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Catalog");

        // Query -> ReadConnection -> replica.
        group.MapGet("/", async (
            ISender sender,
            Guid? shopId,
            string? search,
            ProductStatus? status,
            decimal? minPrice,
            decimal? maxPrice,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default) =>
        {
            var filter = new ProductFilter
            {
                ShopId = shopId,
                SearchTerm = search,
                Status = status,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                Page = page,
                PageSize = pageSize,
            };

            var result = await sender.Send(new GetProductsQuery(filter), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetProducts")
        .WithSummary("Lists products. Served from the read replica.");

        // Query -> ReadConnection by default. `consistency=Strong` routes this
        // one read to the primary, which the admin UI uses right after a write.
        group.MapGet("/{productId:guid}", async (
            ISender sender,
            Guid productId,
            ReadConsistency consistency = ReadConsistency.Eventual,
            CancellationToken cancellationToken = default) =>
        {
            var product = await sender.Send(new GetProductByIdQuery(productId, consistency), cancellationToken);
            return Results.Ok(product);
        })
        .WithName("GetProductById")
        .WithSummary("Gets one product. Pass consistency=Strong to read the primary after a write.");

        // Command -> WriteConnection -> primary, in a transaction.
        group.MapPost("/", async (
            ISender sender,
            CreateProductCommand command,
            CancellationToken cancellationToken) =>
        {
            var productId = await sender.Send(command, cancellationToken);

            // Points at the strongly consistent read: the row was written
            // milliseconds ago and the replica may not have it yet.
            return Results.Created(
                $"/api/products/{productId}?consistency={nameof(ReadConsistency.Strong)}",
                new { id = productId });
        })
        .WithName("CreateProduct")
        .WithSummary("Creates a product and its opening inventory row in one transaction.");

        group.MapPut("/{productId:guid}", async (
            ISender sender,
            Guid productId,
            UpdateProductRequest request,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new UpdateProductCommand(
                    productId,
                    request.Name,
                    request.Sku,
                    request.Description,
                    request.Price),
                cancellationToken);

            return Results.NoContent();
        })
        .WithName("UpdateProduct")
        .WithSummary("Updates a product.");

        group.MapDelete("/{productId:guid}", async (
            ISender sender,
            Guid productId,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteProductCommand(productId), cancellationToken);
            return Results.NoContent();
        })
        .WithName("DeleteProduct")
        .WithSummary("Soft-deletes a product.");

        return app;
    }

    public sealed record UpdateProductRequest(
        string Name,
        string Sku,
        string? Description,
        decimal Price);
}
