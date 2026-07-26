using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Carts.Queries.GetCart;
using OnlineShop.Application.Contracts;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Products.Queries.GetProducts;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Api.Pages.Storefront;

/// <summary>
/// A shop's public catalog.
/// </summary>
/// <remarks>
/// The archetypal replica workload: high-volume browsing where a few seconds of
/// staleness costs nothing. <c>GetProductsQuery</c> offers no consistency
/// option, so this can only ever be served from <c>ReadConnection</c>.
/// </remarks>
[AllowAnonymous]
public sealed class IndexModel : StorefrontPageModel
{
    public IndexModel(ISender sender, IShopDirectory shopDirectory)
        : base(sender, shopDirectory)
    {
    }

    public PagedResult<ProductListItemDto> Products { get; private set; } =
        PagedResult<ProductListItemDto>.Empty(1, 12);

    public int BasketQuantity { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        Products = await Sender.Send(
            new GetProductsQuery(new ProductFilter
            {
                ShopId = Shop!.ShopId,
                Status = ProductStatus.Active,
                SearchTerm = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                Page = Math.Max(PageNumber, 1),
                PageSize = 12,
            }),
            cancellationToken);

        if (CustomerId is { } customerId)
        {
            var cart = await Sender.Send(
                new GetCartQuery(Shop.ShopId, customerId), cancellationToken);
            BasketQuantity = cart?.TotalQuantity ?? 0;
        }

        return Page();
    }
}
