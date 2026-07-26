using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Contracts.Dashboard;
using OnlineShop.Application.Dashboard.Queries.GetShopDashboard;

namespace OnlineShop.Api.Pages.Manage;

/// <summary>
/// Shop analytics.
/// </summary>
/// <remarks>
/// The heaviest read in the application and the one that most wants a replica:
/// several aggregates over orders, order items, customers and inventory. It has
/// no strong-consistency option at all, deliberately — thirty-day totals do not
/// change meaningfully with a few seconds of replication lag, and running these
/// scans on the primary would put reporting in contention with checkout.
/// </remarks>
public sealed class IndexModel : ManagePageModel
{
    public IndexModel(ISender sender)
        : base(sender)
    {
    }

    public ShopDashboardDto? Dashboard { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int WindowDays { get; set; } = 30;

    public async Task<IActionResult> OnGetAsync(Guid? shopId, CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);

        if (CurrentShop is null)
        {
            // A tenant with no shops yet: send them somewhere useful.
            return Page();
        }

        if (WindowDays is not (7 or 30 or 90 or 365))
        {
            WindowDays = 30;
        }

        Dashboard = await Sender.Send(
            new GetShopDashboardQuery(CurrentShop.Id, WindowDays), cancellationToken);

        return Page();
    }
}
