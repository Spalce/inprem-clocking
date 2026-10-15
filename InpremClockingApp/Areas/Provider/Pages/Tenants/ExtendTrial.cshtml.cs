using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Give a trial (or an expired trial) a later end date. Owners and Billing.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class ExtendTrialModel : PageModel
{
    private readonly OrganizationService _organizations;
    private readonly BillingAccountService _billing;

    public ExtendTrialModel(OrganizationService organizations, BillingAccountService billing)
    {
        _organizations = organizations;
        _billing = billing;
    }

    [BindProperty] public DateTime? TrialEnds { get; set; }
    public string Name { get; private set; } = "";
    public DateTime? CurrentEnd { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        TrialEnds = (CurrentEnd > DateTime.UtcNow ? CurrentEnd.Value : DateTime.UtcNow).Date.AddDays(14);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (TrialEnds == null)
        {
            Error = "Choose the new end date.";
            return Page();
        }
        var result = await _billing.ExtendTrialAsync(id, TrialEnds.Value.Date.AddDays(1).AddSeconds(-1), User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }
        TempData["Success"] = $"Trial extended to {TrialEnds:yyyy-MM-dd}.";
        return RedirectToPage("Details", new { id, tab = "subscription" });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return false;
        Name = org.Summary.Name;
        CurrentEnd = org.Subscription?.TrialEndsUtc;
        return true;
    }
}
