using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Add a note to an organization (any provider user).</summary>
public class AddNoteModel : PageModel
{
    private readonly OrganizationService _organizations;

    public AddNoteModel(OrganizationService organizations) => _organizations = organizations;

    [BindProperty] public string Text { get; set; } = "";
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
        var result = await _organizations.AddNoteAsync(id, Text ?? "", User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            Name = (await _organizations.GetAsync(id, HttpContext.RequestAborted))?.Summary.Name ?? "";
            return Page();
        }

        TempData["Success"] = "Note added.";
        return RedirectToPage("Details", new { id, tab = "notes" });
    }
}
