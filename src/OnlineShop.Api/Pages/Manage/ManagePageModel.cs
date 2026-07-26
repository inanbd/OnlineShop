using MediatR;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Contracts.Shops;
using OnlineShop.Application.Shops.Queries.GetShops;

namespace OnlineShop.Api.Pages.Manage;

/// <summary>
/// Shared behaviour for back-office pages: resolving which shop the merchant is
/// currently working in.
/// </summary>
/// <remarks>
/// A tenant can own several shops, so most screens are scoped to one. The
/// choice travels in the query string rather than session state, which keeps
/// every back-office URL shareable and bookmarkable.
/// </remarks>
public abstract class ManagePageModel : PageModel
{
    protected ManagePageModel(ISender sender)
    {
        Sender = sender;
    }

    protected ISender Sender { get; }

    /// <summary>Every shop in the tenant, for the shop switcher.</summary>
    public IReadOnlyList<ShopListItemDto> Shops { get; private set; } = [];

    /// <summary>The shop currently being worked in, if the tenant has any.</summary>
    public ShopListItemDto? CurrentShop { get; private set; }

    /// <summary>Which database served this page's data.</summary>
    public ReadConsistency Consistency { get; protected set; } = ReadConsistency.Eventual;

    /// <summary>
    /// Loads the tenant's shops and settles on the current one.
    /// </summary>
    /// <param name="requestedShopId">The shop from the query string, if any.</param>
    /// <param name="consistency">
    /// Pass <see cref="ReadConsistency.Strong"/> when arriving straight from a
    /// write, so a shop created moments ago is present in the switcher.
    /// </param>
    protected async Task LoadShopsAsync(
        Guid? requestedShopId,
        ReadConsistency consistency = ReadConsistency.Eventual,
        CancellationToken cancellationToken = default)
    {
        Shops = await Sender.Send(new GetShopsQuery(consistency), cancellationToken);

        CurrentShop = requestedShopId is { } shopId
            ? Shops.FirstOrDefault(shop => shop.Id == shopId)
            : null;

        CurrentShop ??= Shops.FirstOrDefault();
    }

    /// <summary>Keeps the selected shop in the URL when linking between pages.</summary>
    protected object? ShopRoute => CurrentShop is null ? null : new { shopId = CurrentShop.Id };
}
