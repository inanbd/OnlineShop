using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Products.Commands.CreateProduct;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Manage.Products;

public sealed class CreateModel : ManagePageModel
{
    public CreateModel(ISender sender)
        : base(sender)
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [StringLength(300, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(64, MinimumLength = 1)]
        [Display(Name = "SKU")]
        public string Sku { get; set; } = string.Empty;

        [StringLength(4000)]
        public string? Description { get; set; }

        [Range(0, 1_000_000)]
        public decimal Price { get; set; }

        [Range(0, 1_000_000)]
        [Display(Name = "Opening stock")]
        public int InitialQuantityOnHand { get; set; }

        [Range(0, 1_000_000)]
        [Display(Name = "Reorder threshold")]
        public int ReorderThreshold { get; set; } = 5;

        [Display(Name = "Publish immediately")]
        public bool PublishImmediately { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync(Guid? shopId, CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);
        return CurrentShop is null ? RedirectToPage("/Manage/Shops/Create") : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? shopId, CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);

        if (CurrentShop is null)
        {
            return RedirectToPage("/Manage/Shops/Create");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        Guid productId;
        try
        {
            productId = await Sender.Send(
                new CreateProductCommand(
                    ShopId: CurrentShop.Id,
                    Name: Input.Name,
                    Sku: Input.Sku,
                    Description: Input.Description,
                    Price: Input.Price,
                    InitialQuantityOnHand: Input.InitialQuantityOnHand,
                    ReorderThreshold: Input.ReorderThreshold,
                    PublishImmediately: Input.PublishImmediately),
                cancellationToken);
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError("Input.Sku", exception.Message);
            return Page();
        }

        TempData["StatusMessage"] = $"'{Input.Name}' was created.";

        // Straight to the edit screen, which reads with strong consistency. The
        // product is milliseconds old, so the replica may not have it yet.
        return RedirectToPage("Edit", new { id = productId, shopId = CurrentShop.Id, justSaved = true });
    }
}
