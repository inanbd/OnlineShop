using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Application.Abstractions.Identity;
using OnlineShop.Application.Registration.Commands.RegisterMerchant;
using OnlineShop.Domain.Common;

namespace OnlineShop.Api.Pages.Account;

/// <summary>
/// Merchant registration: provisions a tenant, its first shop, and the owner
/// account.
/// </summary>
[AllowAnonymous]
public sealed class RegisterModel : PageModel
{
    private readonly ISender _sender;
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        ISender sender,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        ILogger<RegisterModel> logger)
    {
        _sender = sender;
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
        [Display(Name = "Business name")]
        public string BusinessName { get; set; } = string.Empty;

        [Required]
        [StringLength(60, MinimumLength = 2)]
        [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers and hyphens only.")]
        [Display(Name = "Shop address")]
        public string ShopSlug { get; set; } = string.Empty;

        [Required]
        [StringLength(200, MinimumLength = 2)]
        [Display(Name = "Shop name")]
        public string ShopName { get; set; } = string.Empty;

        [Required]
        [SupportedCurrency]
        [Display(Name = "Currency")]
        public string CurrencyCode { get; set; } = CurrencyOptions.Default;

        [Required]
        [StringLength(200, MinimumLength = 2)]
        [Display(Name = "Your name")]
        public string DisplayName { get; set; } = string.Empty;

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

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Checked before provisioning so the common collision does not leave an
        // unreachable tenant behind. The unique index is still the authority if
        // two registrations race.
        if (await _userManager.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError("Input.Email", "An account with that email address already exists.");
            return Page();
        }

        var ownerUserId = Guid.NewGuid();

        RegisterMerchantResult provisioned;
        try
        {
            provisioned = await _sender.Send(new RegisterMerchantCommand(
                BusinessName: Input.BusinessName,
                TenantSlug: Input.ShopSlug,
                ShopName: Input.ShopName,
                ShopSlug: Input.ShopSlug,
                CurrencyCode: Input.CurrencyCode,
                OwnerEmail: Input.Email,
                OwnerUserId: ownerUserId));
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError("Input.ShopSlug", exception.Message);
            return Page();
        }

        var user = new AppUser
        {
            Id = ownerUserId,
            TenantId = provisioned.TenantId,
            Email = Input.Email,
            UserName = Input.Email,
            DisplayName = Input.DisplayName,
            EmailConfirmed = true,
        };

        var created = await _userManager.CreateAsync(user, Input.Password);

        if (!created.Succeeded)
        {
            // The tenant provisioned above is now unreachable: no account can
            // sign in to it, so it holds no data and is visible to nobody. That
            // is preferable to running Identity's password hashing and
            // validation inside the tenant transaction, which would mean
            // reimplementing them.
            _logger.LogWarning(
                "Tenant {TenantId} was provisioned but its owner account could not be created: {Errors}",
                provisioned.TenantId,
                string.Join("; ", created.Errors.Select(e => e.Description)));

            foreach (var error in created.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        await _userManager.AddToRoleAsync(user, AppRoles.Merchant);
        await _signInManager.SignInAsync(user, isPersistent: false);

        TempData["StatusMessage"] = $"Welcome. '{Input.ShopName}' is ready — add your first product to get going.";
        return RedirectToPage("/Manage/Index");
    }
}
