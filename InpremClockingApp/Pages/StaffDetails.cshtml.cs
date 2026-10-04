using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class StaffDetails : PageModel
{
    private readonly StaffService _service;

    public StaffDetails(StaffService service)
    {
        _service = service;
    }

    public Models.Staff? Staff { get; set; }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Staff = await _service.GetById(Id).ConfigureAwait(true);
        if (Staff == null)
        {
            return RedirectToPage("./Staff");
        }

        return Page();
    }
}
