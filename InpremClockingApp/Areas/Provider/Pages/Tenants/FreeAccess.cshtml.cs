using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Grant full access until a date whatever the subscription says (or remove it). Owners and Billing; reason required.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class FreeAccessModel : PageModel
{
    private readonly OrganizationService _organizations;
    private readonly BillingAccountService _billing;

    public FreeAccessModel(OrganizationService organizations, BillingAccountService billing)
    {
        _organizations = organizations;
        _billing = billing;
    }

    [BindProperty] public DateTime? Until { get; set; }
    [BindProperty] public string Reason { get; set; } = "";
    [BindProperty] public bool Remove { get; set; }
    public string Name { get; private set; } = "";
    public DateTime? Current { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        Until = DateTime.UtcNow.Date.AddDays(30);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!Remove && Until == null)
        {
            Error = "Choose the last day of free access.";
            return Page();
        }
        var until = Remove ? (DateTime?)null : Until!.Value.Date.AddDays(1).AddSeconds(-1);
        var result = await _billing.GrantFreeAccessAsync(id, until, Reason ?? "", User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }
        TempData["Success"] = Remove ? "Free access removed." : $"Full access until {Until:yyyy-MM-dd}.";
        return RedirectToPage("Details", new { id, tab = "subscription" });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return false;
        Name = org.Summary.Name;
        Current = (await _billing.GetAsync(id, HttpContext.RequestAborted)).Subscription?.FreeAccessUntilUtc;
        return true;
    }
}
