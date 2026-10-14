using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace InpremClockingApp.Areas.Provider.Pages.Users;

/// <summary>Change a provider user's role (needs a fresh two-factor code).</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
[RequireRecentTwoFactor]
public class RoleModel : ProviderUserPage
{
    public RoleModel(ProviderSecurityService provider) : base(provider)
    {
    }

    [BindProperty] public ProviderRole Role { get; set; }

    public async Task<IActionResult> OnGetAsync(string userId)
    {
        if (!await LoadAsync(userId)) return NotFound();
        Role = Person.Role;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string userId)
    {
        if (!await LoadAsync(userId)) return NotFound();
        var result = await Provider.ChangeProviderRoleAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, userId, Role, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Message;
            return Page();
        }

        TempData["Success"] = result.Message;
        return RedirectToPage("Details", new { userId });
    }
}
