using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Contracts;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Products.Queries.GetProducts;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Api.Pages.Manage.Products;

public sealed class IndexModel : ManagePageModel
{
    public IndexModel(ISender sender)
        : base(sender)
    {
    }

    public PagedResult<ProductListItemDto> Products { get; private set; } =
        PagedResult<ProductListItemDto>.Empty(1, 25);

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProductStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync(Guid? shopId, CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);

        if (CurrentShop is null)
        {
            return Page();
        }

        // Catalog browsing tolerates replication lag, so this stays on the
        // replica. The only reads that do not are the ones straight after a
        // write, handled in Edit and Create.
        Products = await Sender.Send(
            new GetProductsQuery(new ProductFilter
            {
                ShopId = CurrentShop.Id,
                SearchTerm = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                Status = Status,
                IncludeDeleted = false,
                Page = Math.Max(PageNumber, 1),
                PageSize = 25,
            }),
            cancellationToken);

        return Page();
    }
}
