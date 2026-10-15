using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Cancel the organization's PayPal subscription (PayPal first; access to the end of the paid period). Owners and Billing.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class CancelSubscriptionModel : PageModel
{
    private readonly OrganizationService _organizations;
    private readonly CheckoutService _checkout;

    public CancelSubscriptionModel(OrganizationService organizations, CheckoutService checkout)
    {
        _organizations = organizations;
        _checkout = checkout;
    }

    [BindProperty] public string Reason { get; set; } = "";
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
        var result = await _checkout.CancelSubscriptionAsync(id, Reason ?? "", User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }
        TempData["Success"] = "Subscription cancelled in PayPal; the organization keeps access until the end of the paid period.";
        return RedirectToPage("Details", new { id, tab = "subscription" });
    }
}
