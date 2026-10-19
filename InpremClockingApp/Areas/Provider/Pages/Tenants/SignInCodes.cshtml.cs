using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>
/// Texted sign-in codes for an organization's admins (Phase 5 step 5.2): on or off (off by default).
/// A security setting, so Owners only and a recent two-factor code first (design 5.5).
/// </summary>
[Authorize(Policy = ProviderPolicies.Owner)]
[RequireRecentTwoFactor]
public class SignInCodesModel : PageModel
{
    private readonly OrganizationService _organizations;
    private readonly MessagingSettingsService _settings;

    public SignInCodesModel(OrganizationService organizations, MessagingSettingsService settings)
    {
        _organizations = organizations;
        _settings = settings;
    }

    [BindProperty] public bool Required { get; set; }

    public string Name { get; private set; } = "";
    public bool IsOn { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        Name = org.Summary.Name;
        IsOn = await _settings.SmsSignInRequiredAsync(id, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        await _settings.SetSmsSignInAsync(id, Required, User.Actor(), HttpContext.RequestAborted);
        TempData["Success"] = Required
            ? $"{org.Summary.Name}: admins now enter a texted code after their password."
            : $"{org.Summary.Name}: admins sign in with their password only.";
        return RedirectToPage("Details", new { id });
    }
}
