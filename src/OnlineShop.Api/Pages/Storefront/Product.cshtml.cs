using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Carts.Commands.AddToCart;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Products.Queries.GetProductById;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Storefront;

[AllowAnonymous]
public sealed class ProductModel : StorefrontPageModel
{
    public ProductModel(ISender sender, IShopDirectory shopDirectory)
        : base(sender, shopDirectory)
    {
    }

    public ProductDto? Product { get; private set; }

    [BindProperty]
    [Range(1, 999)]
    public int Quantity { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync(string slug, Guid id, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        try
        {
            // An ordinary catalog read: the replica is fine here.
            Product = await Sender.Send(new GetProductByIdQuery(id), cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        return Product.ShopId != Shop!.ShopId ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string slug, Guid id, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        if (CustomerId is not { } customerId)
        {
            return RedirectToPage("Register", new { slug });
        }

        try
        {
            await Sender.Send(
                new AddToCartCommand(Shop!.ShopId, customerId, id, Math.Max(Quantity, 1)),
                cancellationToken);
        }
        catch (DomainException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToPage("Product", new { slug, id });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Added to your basket.";

        // justChanged makes the basket read with strong consistency: the line
        // was written milliseconds ago and the replica may not have it.
        return RedirectToPage("Cart", new { slug, justChanged = true });
    }
}
