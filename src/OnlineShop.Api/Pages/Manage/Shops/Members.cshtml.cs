using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Contracts.Shops;
using OnlineShop.Application.Shops.Commands.InviteShopMember;
using OnlineShop.Application.Shops.Queries.GetShopMembers;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Shops;

namespace OnlineShop.Api.Pages.Manage.Shops;

public sealed class MembersModel : ManagePageModel
{
    public MembersModel(ISender sender)
        : base(sender)
    {
    }

    public IReadOnlyList<ShopMemberDto> Members { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Role")]
        public ShopMemberRole Role { get; set; } = ShopMemberRole.Staff;
    }

    public async Task<IActionResult> OnGetAsync(
        Guid? shopId,
        bool justInvited,
        CancellationToken cancellationToken)
    {
        Consistency = justInvited ? ReadConsistency.Strong : ReadConsistency.Eventual;

        await LoadShopsAsync(shopId, Consistency, cancellationToken);

        if (CurrentShop is null)
        {
            return RedirectToPage("Create");
        }

        Members = await Sender.Send(
            new GetShopMembersQuery(CurrentShop.Id, Consistency), cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? shopId, CancellationToken cancellationToken)
    {
        await LoadShopsAsync(shopId, ReadConsistency.Strong, cancellationToken);

        if (CurrentShop is null)
        {
            return RedirectToPage("Create");
        }

        if (!ModelState.IsValid)
        {
            Members = await Sender.Send(
                new GetShopMembersQuery(CurrentShop.Id, ReadConsistency.Strong), cancellationToken);
            return Page();
        }

        var invitedBy = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : Guid.NewGuid();

        try
        {
            await Sender.Send(
                new InviteShopMemberCommand(CurrentShop.Id, Input.Email, Input.Role, invitedBy),
                cancellationToken);
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError("Input.Email", exception.Message);
            Members = await Sender.Send(
                new GetShopMembersQuery(CurrentShop.Id, ReadConsistency.Strong), cancellationToken);
            return Page();
        }

        TempData["StatusMessage"] = $"Invited {Input.Email}.";
        return RedirectToPage("Members", new { shopId = CurrentShop.Id, justInvited = true });
    }
}
