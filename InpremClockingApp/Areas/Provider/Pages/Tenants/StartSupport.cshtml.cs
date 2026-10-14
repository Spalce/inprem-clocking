using System.Security.Claims;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Application.Support;
using Inprem.Platform.Domain.Support;
using InpremClockingApp.Provider;
using InpremClockingApp.Provider.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Start a support session (Owners and Support; reason required; needs a fresh two-factor code).</summary>
[Authorize(Policy = ProviderPolicies.SupportSessions)]
[RequireRecentTwoFactor]
public class StartSupportModel : PageModel
{
    private readonly SupportSessionService _support;
    private readonly OrganizationService _organizations;
    private readonly IConfiguration _configuration;

    public StartSupportModel(SupportSessionService support, OrganizationService organizations, IConfiguration configuration)
    {
        _support = support;
        _organizations = organizations;
        _configuration = configuration;
    }

    [BindProperty] public string Reason { get; set; } = "";
    [BindProperty] public int Minutes { get; set; } = SupportSession.DefaultMinutes;
    public string Name { get; private set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        Name = org.Summary.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        Name = org.Summary.Name;

        var configured = _configuration["Links:AppBaseUrl"];
        var appBase = !string.IsNullOrWhiteSpace(configured) ? configured : OrganizationUi.AppLinkBase(HttpContext);
        var result = await _support.StartAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, Reason ?? "", Minutes, appBase, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        // Straight to the App site's single-use hand-off (60 seconds).
        return Redirect(result.HandoffUrl!);
    }
}
