using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Application.Orders.Queries.GetOrderDetails;

namespace OnlineShop.Api.Pages.Storefront;

/// <summary>
/// The order confirmation page.
/// </summary>
/// <remarks>
/// The single clearest justification for the strong-consistency escape hatch in
/// the whole application. Checkout redirects here within milliseconds of the
/// write transaction committing, and a replica that has not caught up would
/// show the shopper "order not found" for the purchase they have just paid for.
/// <c>GetOrderDetailsQuery</c> carries a documented allowance for exactly this.
/// </remarks>
[Authorize]
public sealed class OrderConfirmationModel : StorefrontPageModel
{
    public OrderConfirmationModel(ISender sender, IShopDirectory shopDirectory)
        : base(sender, shopDirectory)
    {
    }

    public OrderDetailsDto? Order { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug, Guid id, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        Consistency = ReadConsistency.Strong;

        try
        {
            Order = await Sender.Send(new GetOrderDetailsQuery(id, Consistency), cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        // A shopper must not be able to read someone else's order by changing
        // the id in the URL.
        if (Order.ShopId != Shop!.ShopId || Order.CustomerId != CustomerId)
        {
            return NotFound();
        }

        return Page();
    }
}
