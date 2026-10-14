using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Reactivate an organization: says what happens, asks for a reason, then confirms.</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class ReactivateModel : PageModel
{
    private readonly OrganizationService _organizations;

    public ReactivateModel(OrganizationService organizations) => _organizations = organizations;

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
        var result = await _organizations.ReactivateAsync(id, Reason ?? "", User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Success"] = "Organization reactivated.";
        return RedirectToPage("Details", new { id });
    }
}
