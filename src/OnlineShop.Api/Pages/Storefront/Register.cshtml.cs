using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions.Identity;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Registration.Commands.RegisterShopper;

namespace OnlineShop.Api.Pages.Storefront;

/// <summary>
/// Shopper registration, scoped to the storefront being browsed.
/// </summary>
/// <remarks>
/// The tenant comes from the shop slug, resolved before anything is written, so
/// the customer record lands in the right tenant without the browser ever
/// naming one.
/// </remarks>
[AllowAnonymous]
public sealed class RegisterModel : StorefrontPageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        ISender sender,
        IShopDirectory shopDirectory,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        ILogger<RegisterModel> logger)
        : base(sender, shopDirectory)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [StringLength(200, MinimumLength = 2)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        return CanShop ? RedirectToPage("Index", new { slug }) : Page();
    }

    public async Task<IActionResult> OnPostAsync(string slug, CancellationToken cancellationToken)
    {
        if (!await ResolveShopAsync(slug, cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (await _userManager.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError("Input.Email", "An account with that email address already exists.");
            return Page();
        }

        // Creates the shop-side customer record inside the tenant this
        // storefront resolved to.
        var customerId = await Sender.Send(
            new RegisterShopperCommand(Shop!.ShopId, Input.Email, Input.FullName),
            cancellationToken);

        var user = new AppUser
        {
            TenantId = Shop.TenantId,
            Email = Input.Email,
            UserName = Input.Email,
            DisplayName = Input.FullName,
            EmailConfirmed = true,
            ShopId = Shop.ShopId,
            CustomerId = customerId,
        };

        var created = await _userManager.CreateAsync(user, Input.Password);

        if (!created.Succeeded)
        {
            _logger.LogWarning(
                "Customer {CustomerId} was created for shop {ShopId} but the sign-in account failed: {Errors}",
                customerId,
                Shop.ShopId,
                string.Join("; ", created.Errors.Select(e => e.Description)));

            foreach (var error in created.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        await _userManager.AddToRoleAsync(user, AppRoles.Shopper);
        await _signInManager.SignInAsync(user, isPersistent: false);

        TempData["StatusMessage"] = $"Welcome to {Shop.Name}.";
        return RedirectToPage("Index", new { slug });
    }
}
