using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Services;
using InpremClockingApp.Models;

namespace InpremClockingApp.Pages;

public class VolunteerClockingReport : PageModel
{
    private readonly VolunteerClockingService _service;
    public List<VolunteerClockingVm> ReportRows { get; set; } = new List<VolunteerClockingVm>();

    public VolunteerClockingReport(VolunteerClockingService service)
    {
        _service = service;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var start = DateTime.Now.Date.AddDays(-30);
        var end = DateTime.Now.Date;
        ReportRows = await _service.GetClockingReport(start, end).ConfigureAwait(false);
        return Page();
    }
}
