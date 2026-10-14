using Inprem.Platform.Application.Provider;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace InpremClockingApp.Areas.Provider.Pages.Users;

/// <summary>Disable a provider user, with a reason (needs a fresh two-factor code).</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
[RequireRecentTwoFactor]
public class DisableModel : ProviderUserPage
{
    public DisableModel(ProviderSecurityService provider) : base(provider)
    {
    }

    [BindProperty] public string Reason { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(string userId) => await LoadAsync(userId) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(string userId)
    {
        if (!await LoadAsync(userId)) return NotFound();
        var result = await Provider.DisableProviderUserAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, userId, Reason ?? "", HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Message;
            return Page();
        }

        TempData["Success"] = result.Message;
        return RedirectToPage("Details", new { userId });
    }
}
