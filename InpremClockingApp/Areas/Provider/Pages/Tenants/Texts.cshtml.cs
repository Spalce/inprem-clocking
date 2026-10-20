using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>
/// An organization's texts (Phase 5 step 5.3): on or off, and the most a month. SMS settings are a
/// sensitive action (design 5.5): Owners, with a recent two-factor code.
/// </summary>
[Authorize(Policy = ProviderPolicies.Owner)]
[RequireRecentTwoFactor]
public class TextsModel : PageModel
{
    private readonly OrganizationService _organizations;
    private readonly NotificationConsole _console;

    public TextsModel(OrganizationService organizations, NotificationConsole console)
    {
        _organizations = organizations;
        _console = console;
    }

    [BindProperty] public bool SmsEnabled { get; set; }
    [BindProperty] public int MonthlyLimit { get; set; }

    public string Name { get; private set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        Name = org.Summary.Name;
        (SmsEnabled, MonthlyLimit) = await _console.TextsSettingsAsync(id, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        Name = org.Summary.Name;
        Error = await _console.SetTextsAsync(id, SmsEnabled, MonthlyLimit, User.Actor(), HttpContext.RequestAborted);
        if (Error != null) return Page();
        TempData["Success"] = $"{org.Summary.Name}: texts {(SmsEnabled ? "on" : "off")}, at most {MonthlyLimit} a month.";
        return RedirectToPage("Details", new { id });
    }
}
