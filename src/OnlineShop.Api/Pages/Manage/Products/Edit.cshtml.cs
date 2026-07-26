using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Contracts.Products;
using OnlineShop.Application.Products.Commands.DeleteProduct;
using OnlineShop.Application.Products.Commands.UpdateProduct;
using OnlineShop.Application.Products.Queries.GetProductById;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Manage.Products;

/// <summary>
/// Edit a product.
/// </summary>
/// <remarks>
/// The clearest read-after-write case in the back-office. Arriving here from
/// Create or from a save means the row was written milliseconds ago, so the
/// read asks for <see cref="ReadConsistency.Strong"/> and is served by the
/// primary. Opening the same page from the product list is an ordinary replica
/// read. The badge on the page shows which of the two happened.
/// </remarks>
public sealed class EditModel : ManagePageModel
{
    public EditModel(ISender sender)
        : base(sender)
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public ProductDto? Product { get; private set; }

    public sealed class InputModel
    {
        public Guid Id { get; set; }

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
    }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        Guid? shopId,
        bool justSaved,
        CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);

        // Only pay for a primary read when we have actually just written.
        Consistency = justSaved ? ReadConsistency.Strong : ReadConsistency.Eventual;

        try
        {
            Product = await Sender.Send(new GetProductByIdQuery(id, Consistency), cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = Product.Id,
            Name = Product.Name,
            Sku = Product.Sku,
            Description = Product.Description,
            Price = Product.Price,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? shopId, CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, cancellationToken: cancellationToken);

        if (!ModelState.IsValid)
        {
            Product = await Sender.Send(
                new GetProductByIdQuery(Input.Id, ReadConsistency.Strong), cancellationToken);
            return Page();
        }

        try
        {
            await Sender.Send(
                new UpdateProductCommand(
                    ProductId: Input.Id,
                    Name: Input.Name,
                    Sku: Input.Sku,
                    Description: Input.Description,
                    Price: Input.Price),
                cancellationToken);
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError("Input.Sku", exception.Message);
            Product = await Sender.Send(
                new GetProductByIdQuery(Input.Id, ReadConsistency.Strong), cancellationToken);
            return Page();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = $"'{Input.Name}' was saved.";
        return RedirectToPage("Edit", new { id = Input.Id, shopId = CurrentShop?.Id, justSaved = true });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, Guid? shopId, CancellationToken cancellationToken)
    {
        try
        {
            await Sender.Send(new DeleteProductCommand(id), cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Product deleted. Past orders still show it as it was sold.";
        return RedirectToPage("Index", new { shopId });
    }
}
