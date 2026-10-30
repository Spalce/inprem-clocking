using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace InpremClockingApp.Areas.Provider.Pages.Users;

/// <summary>Invite a provider user (design 5.5: needs a fresh two-factor code).</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
[RequireRecentTwoFactor]
public class InviteModel : ProviderUserPage
{
    public InviteModel(ProviderSecurityService provider) : base(provider)
    {
    }

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public ProviderRole Role { get; set; } = ProviderRole.Support;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await Provider.InviteProviderUserAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, Email, Role, ConsoleLinkBase(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Message;
            return Page();
        }

        TempData["Success"] = result.Message;
        if (!string.IsNullOrWhiteSpace(result.TemporaryPassword))
            TempData["TemporaryPassword"] = result.TemporaryPassword;
        return RedirectToPage("Index");
    }
}
