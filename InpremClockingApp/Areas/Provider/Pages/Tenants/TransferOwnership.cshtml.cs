using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Make an active admin the organization's owner (Owners only; needs a fresh two-factor code).</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
[RequireRecentTwoFactor]
public class TransferOwnershipModel : PageModel
{
    private readonly OrganizationService _organizations;

    public TransferOwnershipModel(OrganizationService organizations) => _organizations = organizations;

    [BindProperty(SupportsGet = true)] public string UserId { get; set; } = "";
    public string Name { get; private set; } = "";
    public string NewOwner { get; private set; } = "";
    public string CurrentOwners { get; private set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var result = await _organizations.TransferOwnershipAsync(id, UserId, User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Success"] = $"{NewOwner} is now the owner.";
        return RedirectToPage("Details", new { id, tab = "admins" });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var org = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        var admin = org?.Admins.FirstOrDefault(a => a.UserId == UserId);
        if (org == null || admin == null) return false;
        Name = org.Summary.Name;
        NewOwner = admin.Email;
        CurrentOwners = org.Summary.OwnerEmails.Count == 0 ? "nobody" : string.Join(", ", org.Summary.OwnerEmails);
        return true;
    }
}
