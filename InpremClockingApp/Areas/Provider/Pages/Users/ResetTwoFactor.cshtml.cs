using Inprem.Platform.Application.Provider;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace InpremClockingApp.Areas.Provider.Pages.Users;

/// <summary>
/// Recovery Level 2, peer reset (design 5.3): another Provider Owner resets the person's two-factor,
/// entering a current code of their own and a reason. All owners and the security mailbox are told.
/// </summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class ResetTwoFactorModel : ProviderUserPage
{
    public ResetTwoFactorModel(ProviderSecurityService provider) : base(provider)
    {
    }

    [BindProperty] public string Reason { get; set; } = "";
    [BindProperty] public string Code { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(string userId) => await LoadAsync(userId) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(string userId)
    {
        if (!await LoadAsync(userId)) return NotFound();
        var result = await Provider.PeerResetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, userId, Reason ?? "", AccountGateway.NormalizeCode(Code ?? ""), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Message;
            Code = "";
            return Page();
        }

        TempData["Success"] = result.Message;
        return RedirectToPage("Details", new { userId });
    }
}
