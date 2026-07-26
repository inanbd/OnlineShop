using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Contracts;
using OnlineShop.Application.Contracts.Orders;
using OnlineShop.Application.Orders.Queries.GetOrders;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Api.Pages.Manage.Orders;

public sealed class IndexModel : ManagePageModel
{
    public IndexModel(ISender sender)
        : base(sender)
    {
    }

    public PagedResult<OrderListItemDto> Orders { get; private set; } =
        PagedResult<OrderListItemDto>.Empty(1, 25);

    [BindProperty(SupportsGet = true)]
    public OrderStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? OrderNumber { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync(Guid? shopId, CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);

        if (CurrentShop is null)
        {
            return Page();
        }

        // Reporting over a potentially large table: exactly what the replica is
        // for. No consistency option on this query at all.
        Orders = await Sender.Send(
            new GetOrdersQuery(new OrderFilter
            {
                ShopId = CurrentShop.Id,
                Status = Status,
                OrderNumber = string.IsNullOrWhiteSpace(OrderNumber) ? null : OrderNumber.Trim(),
                Page = Math.Max(PageNumber, 1),
                PageSize = 25,
            }),
            cancellationToken);

        return Page();
    }
}
