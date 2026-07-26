using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Api.Identity;
using OnlineShop.Application.Shops.Commands.CreateShop;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Manage.Shops;

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
        [StringLength(200, MinimumLength = 2)]
        [Display(Name = "Shop name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(60, MinimumLength = 2)]
        [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers and hyphens only.")]
        [Display(Name = "Address")]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [SupportedCurrency]
        [Display(Name = "Currency")]
        public string CurrencyCode { get; set; } = CurrencyOptions.Default;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadShopsAsync(null, cancellationToken: cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadShopsAsync(null, cancellationToken: cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ownerUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : Guid.NewGuid();

        Guid shopId;
        try
        {
            shopId = await Sender.Send(
                new CreateShopCommand(
                    Name: Input.Name,
                    Slug: Input.Slug,
                    CurrencyCode: Input.CurrencyCode,
                    OwnerUserId: ownerUserId,
                    OwnerEmail: User.FindFirstValue(ClaimTypes.Email) ?? "owner@example.test"),
                cancellationToken);
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError("Input.Slug", exception.Message);
            return Page();
        }

        TempData["StatusMessage"] = $"'{Input.Name}' was created.";
        return RedirectToPage("/Manage/Index", new { shopId, justCreated = true });
    }
}
