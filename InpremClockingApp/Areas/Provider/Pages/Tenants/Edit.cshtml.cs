using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Edit an organization's own details. Save and Cancel; back to the organization.</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class EditModel : PageModel
{
    private readonly OrganizationService _organizations;

    public EditModel(OrganizationService organizations) => _organizations = organizations;

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Slug { get; set; } = "";
    [BindProperty] public string TimeZoneId { get; set; } = "";
    [BindProperty] public string? Address { get; set; }
    [BindProperty] public string? ContactInfo { get; set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (org == null) return NotFound();
        (Name, Slug, TimeZoneId, Address, ContactInfo) = (org.Summary.Name, org.Summary.Slug, org.TimeZoneId, org.Address, org.ContactInfo);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var result = await _organizations.UpdateAsync(id, new OrganizationEdit(Name, Slug, TimeZoneId, Address, ContactInfo), User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Success"] = "Organization saved.";
        return RedirectToPage("Details", new { id });
    }
}
