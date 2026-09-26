using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Helpers;
using InpremClockingApp.Services;
using InpremClockingApp.Models;

namespace InpremClockingApp.Pages;

public class OneVolunteerClockingReport : PageModel
{
    private readonly VolunteerClockingService _service;
    public List<VolunteerClockingVm> ReportRows { get; set; } = new List<VolunteerClockingVm>();

    public int VolunteerId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public OneVolunteerClockingReport(VolunteerClockingService service)
    {
        _service = service;
    }

    public async Task OnGetAsync(int volunteerId)
    {
        VolunteerId = volunteerId;
        Start = OrgClock.NowLocal().Date.AddDays(-30);
        End = OrgClock.NowLocal().Date;
        ReportRows = await _service.GetClockingReportForVolunteer(volunteerId, Start, End).ConfigureAwait(false);
    }
}
