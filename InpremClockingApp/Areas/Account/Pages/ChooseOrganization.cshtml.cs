using System.Security.Claims;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Account.Pages;

/// <summary>
/// C3-5: shown only to someone with more than one Active membership, after sign-in (the
/// tenant-context middleware sends them here). Choosing signs them in again with that
/// organization's membership on the cookie.
/// </summary>
[Authorize]
public class ChooseOrganizationModel : PageModel
{
    private readonly IMembershipService _memberships;
    private readonly SignInManager<AppUser> _signInManager;

    public ChooseOrganizationModel(IMembershipService memberships, SignInManager<AppUser> signInManager)
    {
        _memberships = memberships;
        _signInManager = signInManager;
    }

    public IReadOnlyList<ActiveMembershipInfo> Memberships { get; private set; } = Array.Empty<ActiveMembershipInfo>();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Memberships = await _memberships.ListActiveAsync(UserId);
        return Memberships.Count <= 1 ? LocalRedirect(Destination) : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid membershipId)
    {
        var chosen = (await _memberships.ListActiveAsync(UserId)).FirstOrDefault(m => m.MembershipId == membershipId);
        var user = await _signInManager.UserManager.GetUserAsync(User);
        if (chosen == null || user == null)
        {
            return RedirectToPage(new { ReturnUrl });
        }

        var current = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        await _signInManager.SignInWithClaimsAsync(user, current.Properties, new[]
        {
            new Claim(TenantClaimTypes.TenantId, chosen.TenantId.ToString()),
            new Claim(PlatformClaimTypes.MembershipId, chosen.MembershipId.ToString()),
        });

        return LocalRedirect(Destination);
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Same default destination as the login page.
    private string Destination =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) && !ReturnUrl.StartsWith("/Account/ChooseOrganization", StringComparison.OrdinalIgnoreCase)
            ? ReturnUrl
            : Url.Content("~/VolunteerAttendance");
}
