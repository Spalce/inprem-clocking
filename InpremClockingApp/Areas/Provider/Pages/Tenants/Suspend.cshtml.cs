using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Suspend an organization: says what happens, asks for a reason, then confirms.</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class SuspendModel : PageModel
{
    private readonly OrganizationService _organizations;

    public SuspendModel(OrganizationService organizations) => _organizations = organizations;

    [BindProperty] public string Reason { get; set; } = "";

    /// <summary>Put the reason in the email to the owners (off by default: some reasons, e.g. fraud, are not shared).</summary>
    [BindProperty] public bool ShareReason { get; set; }

    public string Name { get; private set; } = "";

    /// <summary>PayPal keeps charging a suspended organization: shown as a warning before suspending.</summary>
    public bool PaysByPayPal { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        Load(org);
        return Page();
    }

    private void Load(OrganizationDetail org)
    {
        Name = org.Summary.Name;
        PaysByPayPal = org.Subscription is { Mode: Inprem.Platform.Domain.Billing.BillingMode.PayPal, Status: Inprem.Platform.Domain.Billing.SubscriptionStatus.Active or Inprem.Platform.Domain.Billing.SubscriptionStatus.PastDue };
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        Load(org);
        var result = await _organizations.SuspendAsync(id, Reason ?? "", User.Actor(), HttpContext.RequestAborted, ShareReason);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Success"] = result.Warning == null ? "Organization suspended; its owners were emailed." : $"Organization suspended. {result.Warning}";
        return RedirectToPage("Details", new { id });
    }
}
