using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Application.Abstractions.Identity;

namespace OnlineShop.Api.Pages.Account;

[AllowAnonymous]
public sealed class LogoutModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;

    public LogoutModel(SignInManager<AppUser> signInManager)
    {
        _signInManager = signInManager;
    }

    /// <summary>
    /// POST only. Signing out on GET would let any page on the internet drop a
    /// user's session with an image tag.
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await _signInManager.SignOutAsync();
        TempData["StatusMessage"] = "You have been signed out.";
        return RedirectToPage("/Index");
    }

    public IActionResult OnGet() => RedirectToPage("/Index");
}
