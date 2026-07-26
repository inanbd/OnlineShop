using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Application.Orders.Commands.CancelOrder;
using OnlineShop.Application.Orders.Queries.GetOrderDetails;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Manage.Orders;

public sealed class DetailsModel : ManagePageModel
{
    public DetailsModel(ISender sender)
        : base(sender)
    {
    }

    public OrderDetailsDto? Order { get; private set; }

    [BindProperty]
    [Required(ErrorMessage = "Give a reason for the cancellation.")]
    [StringLength(500, MinimumLength = 3)]
    public string CancelReason { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        Guid? shopId,
        bool justChanged,
        CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);

        // Only the view that follows a cancellation needs the primary.
        Consistency = justChanged ? ReadConsistency.Strong : ReadConsistency.Eventual;

        try
        {
            Order = await Sender.Send(new GetOrderDetailsQuery(id, Consistency), cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, Guid? shopId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadShopsAsync(shopId, cancellationToken: cancellationToken);
            Consistency = ReadConsistency.Strong;
            Order = await Sender.Send(new GetOrderDetailsQuery(id, Consistency), cancellationToken);
            return Page();
        }

        try
        {
            await Sender.Send(new CancelOrderCommand(id, CancelReason), cancellationToken);
        }
        catch (DomainException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToPage("Details", new { id, shopId, justChanged = true });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Order cancelled and its stock reservation released.";
        return RedirectToPage("Details", new { id, shopId, justChanged = true });
    }
}
