using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class VolunteerDetails : PageModel
{
    private readonly VolunteerService _service;

    public VolunteerDetails(VolunteerService service)
    {
        _service = service;
    }

    public Models.Volunteer? Volunteer { get; set; }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Volunteer = await _service.GetById(Id).ConfigureAwait(true);
        if (Volunteer == null)
        {
            return RedirectToPage("./Volunteer");
        }

        return Page();
    }
}
