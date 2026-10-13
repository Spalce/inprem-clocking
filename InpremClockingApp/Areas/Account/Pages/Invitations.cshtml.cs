using System.Security.Claims;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Account.Pages;

/// <summary>
/// C3-6 (Phase 0 step 0.4): someone who already had an account and was added as an admin of
/// another organization accepts or declines that invitation here, after signing in with their
/// own account. Accepting re-signs them in so the new membership counts at once (with more than
/// one organization they then choose on "Choose organization").
/// </summary>
[Authorize]
public class InvitationsModel : PageModel
{
    private readonly IMembershipService _memberships;
    private readonly SignInManager<AppUser> _signInManager;

    public InvitationsModel(IMembershipService memberships, SignInManager<AppUser> signInManager)
    {
        _memberships = memberships;
        _signInManager = signInManager;
    }

    public IReadOnlyList<ActiveMembershipInfo> Invitations { get; private set; } = Array.Empty<ActiveMembershipInfo>();

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Invitations = await _memberships.ListInvitationsAsync(UserId);
    }

    public async Task<IActionResult> OnPostAcceptAsync(Guid membershipId)
    {
        if (!await _memberships.AcceptAsync(UserId, membershipId))
        {
            return RedirectToPage();
        }

        var user = await _signInManager.UserManager.FindByIdAsync(UserId);
        if (user != null)
        {
            await _signInManager.RefreshSignInAsync(user);
        }

        return LocalRedirect(Url.Content("~/VolunteerAttendance"));
    }

    public async Task<IActionResult> OnPostDeclineAsync(Guid membershipId)
    {
        var name = await NameOfAsync(membershipId);
        if (await _memberships.DeclineAsync(UserId, membershipId))
        {
            Message = $"You declined the invitation from {name}.";
        }
        return RedirectToPage();
    }

    private async Task<string> NameOfAsync(Guid membershipId) =>
        (await _memberships.ListInvitationsAsync(UserId)).FirstOrDefault(i => i.MembershipId == membershipId)?.TenantName ?? "the organization";

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
