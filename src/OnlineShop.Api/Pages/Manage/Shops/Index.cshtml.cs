using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions.Persistence;

namespace OnlineShop.Api.Pages.Manage.Shops;

public sealed class IndexModel : ManagePageModel
{
    public IndexModel(ISender sender)
        : base(sender)
    {
    }

    public async Task<IActionResult> OnGetAsync(
        Guid? shopId,
        bool justCreated,
        CancellationToken cancellationToken)
    {
        // Arriving straight from Create means the shop is milliseconds old.
        Consistency = justCreated ? ReadConsistency.Strong : ReadConsistency.Eventual;

        await LoadShopsAsync(shopId, Consistency, cancellationToken);
        return Page();
    }
}
