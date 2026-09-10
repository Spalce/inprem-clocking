using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Services;
using InpremClockingApp.Models;

namespace InpremClockingApp.Pages;

public class OneVolunteerClockingReport : PageModel
{
    private readonly VolunteerClockingService _service;
    public List<VolunteerClockingVm> ReportRows { get; set; } = new List<VolunteerClockingVm>();

    public OneVolunteerClockingReport(VolunteerClockingService service)
    {
        _service = service;
    }

    public async Task OnGetAsync(int volunteerId)
    {
        var start = DateTime.Now.Date.AddDays(-30);
        var end = DateTime.Now.Date;
        ReportRows = await _service.GetClockingReportForVolunteer(volunteerId, start, end).ConfigureAwait(false);
    }
}
