using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Carts.Queries.GetCart;
using OnlineShop.Application.Contracts.Carts;
using OnlineShop.Application.Orders.Commands.PlaceOrder;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Storefront;

/// <summary>
/// Checkout.
/// </summary>
/// <remarks>
/// The POST runs <c>PlaceOrderCommand</c>, which writes the order, its items,
/// the stock reservation, the payment record, the cart closure and the opening
/// status history under one transaction on the write connection. If stock has
/// run out since the basket was filled, the whole thing rolls back and the
/// shopper is told — there is no half-placed order to clean up.
/// </remarks>
[Authorize]
public sealed class CheckoutModel : StorefrontPageModel
{
    public CheckoutModel(ISender sender, IShopDirectory shopDirectory)
        : base(sender, shopDirectory)
    {
    }

    public CartDto? Cart { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        if (CustomerId is not { } customerId)
        {
            return RedirectToPage("Register", new { slug });
        }

        // The basket is about to be turned into an order, so this read has to
        // reflect the primary rather than a replica that may be behind.
        Consistency = ReadConsistency.Strong;

        Cart = await Sender.Send(
            new GetCartQuery(Shop!.ShopId, customerId, Consistency), cancellationToken);

        if (Cart is null || Cart.IsEmpty)
        {
            return RedirectToPage("Cart", new { slug });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string slug, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        if (CustomerId is not { } customerId)
        {
            return RedirectToPage("Register", new { slug });
        }

        Consistency = ReadConsistency.Strong;

        Cart = await Sender.Send(
            new GetCartQuery(Shop!.ShopId, customerId, Consistency), cancellationToken);

        if (Cart is null || Cart.IsEmpty)
        {
            return RedirectToPage("Cart", new { slug });
        }

        PlaceOrderResult placed;
        try
        {
            placed = await Sender.Send(
                new PlaceOrderCommand(
                    ShopId: Shop.ShopId,
                    CustomerId: customerId,
                    CartId: Cart.Id,
                    PaymentProvider: "demo-provider",

                    // Unique per attempt. The unique index on
                    // (TenantId, Provider, ProviderReference) is what stops a
                    // retried payment from producing a second order.
                    PaymentReference: $"demo-{Guid.NewGuid():N}"),
                cancellationToken);
        }
        catch (DomainException exception)
        {
            // Stock ran out, or the basket changed underneath. Nothing was
            // written: the transaction rolled back in full.
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToPage("Cart", new { slug, justChanged = true });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        // The confirmation reads with strong consistency: this order is
        // milliseconds old and the replica will not have it.
        return RedirectToPage("OrderConfirmation", new { slug, id = placed.OrderId });
    }
}
