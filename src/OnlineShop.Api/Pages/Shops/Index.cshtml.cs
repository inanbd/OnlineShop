using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Application.Abstractions.Persistence.Queries;

namespace OnlineShop.Api.Pages.Shops;

/// <summary>
/// The public list of storefronts.
/// </summary>
/// <remarks>
/// Uses <see cref="IShopDirectory"/>, the one read that spans tenants, and
/// exposes only what a shop front already discloses publicly.
/// </remarks>
[AllowAnonymous]
public sealed class IndexModel : PageModel
{
    private readonly IShopDirectory _shopDirectory;

    public IndexModel(IShopDirectory shopDirectory)
    {
        _shopDirectory = shopDirectory;
    }

    public IReadOnlyList<ShopDirectoryEntry> Shops { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Shops = await _shopDirectory.ListActiveAsync(cancellationToken);
    }
}
