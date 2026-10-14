using Inprem.Platform.Application.Provider;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace InpremClockingApp.Areas.Provider.Pages.Users;

/// <summary>One provider user, read-only; actions open their own pages (resend is a button here).</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class DetailsModel : ProviderUserPage
{
    public DetailsModel(ProviderSecurityService provider) : base(provider)
    {
    }

    public bool IsSelf { get; private set; }

    public async Task<IActionResult> OnGetAsync(string userId)
    {
        if (!await LoadAsync(userId)) return NotFound();
        IsSelf = userId == User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(string userId)
    {
        var result = await Provider.ResendProviderInvitationAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, userId, ConsoleLinkBase(), HttpContext.RequestAborted);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToPage(new { userId });
    }
}
