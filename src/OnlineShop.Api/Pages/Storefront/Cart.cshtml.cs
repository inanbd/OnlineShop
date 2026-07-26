using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Carts.Commands.RemoveCartItem;
using OnlineShop.Application.Carts.Commands.UpdateCartItem;
using OnlineShop.Application.Carts.Queries.GetCart;
using OnlineShop.Application.Contracts.Carts;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Storefront;

/// <summary>
/// The shopper's basket.
/// </summary>
/// <remarks>
/// The most frequently hit read-after-write path in the application. Arriving
/// here from an add, a quantity change or a removal means the write happened
/// milliseconds ago, so the read asks for <see cref="ReadConsistency.Strong"/>
/// and goes to the primary. Reaching the basket from a navigation link is an
/// ordinary replica read. The badge in the header shows which one happened.
/// </remarks>
[Authorize]
public sealed class CartModel : StorefrontPageModel
{
    public CartModel(ISender sender, IShopDirectory shopDirectory)
        : base(sender, shopDirectory)
    {
    }

    public CartDto? Cart { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string slug,
        bool justChanged,
        CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        if (CustomerId is not { } customerId)
        {
            return RedirectToPage("Register", new { slug });
        }

        Consistency = justChanged ? ReadConsistency.Strong : ReadConsistency.Eventual;

        Cart = await Sender.Send(
            new GetCartQuery(Shop!.ShopId, customerId, Consistency), cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        string slug,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
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
                new UpdateCartItemCommand(Shop!.ShopId, customerId, productId, quantity),
                cancellationToken);
        }
        catch (DomainException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        return RedirectToPage("Cart", new { slug, justChanged = true });
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        string slug,
        Guid productId,
        CancellationToken cancellationToken)
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
                new RemoveCartItemCommand(Shop!.ShopId, customerId, productId), cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Removed from your basket.";
        return RedirectToPage("Cart", new { slug, justChanged = true });
    }
}
