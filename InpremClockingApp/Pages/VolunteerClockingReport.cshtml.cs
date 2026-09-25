using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Helpers;
using InpremClockingApp.Services;
using InpremClockingApp.Models;

namespace InpremClockingApp.Pages;

public class VolunteerClockingReport : PageModel
{
    private readonly VolunteerClockingService _service;
    private readonly VolunteerService _volunteerService;
    public List<VolunteerClockingVm> ReportRows { get; set; } = new List<VolunteerClockingVm>();
    public IEnumerable<InpremClockingApp.Models.Volunteer>? Volunteers { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? VolunteerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? End { get; set; }

    public VolunteerClockingReport(VolunteerClockingService service, VolunteerService volunteerService)
    {
        _service = service;
        _volunteerService = volunteerService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var start = Start ?? OrgClock.NowLocal().Date.AddDays(-30);
        var end = End ?? OrgClock.NowLocal().Date.AddDays(1).AddTicks(-1);

        if (VolunteerId.HasValue)
            ReportRows = await _service.GetClockingReportForVolunteer(VolunteerId.Value, start, end).ConfigureAwait(false);
        else
            ReportRows = await _service.GetClockingReport(start, end).ConfigureAwait(false);

        ViewData["VolunteerId"] = VolunteerId;
        ViewData["Start"] = start.ToString("yyyy-MM-ddTHH:mm");
        ViewData["End"] = end.ToString("yyyy-MM-ddTHH:mm");

        // Validation: ensure start <= end when both provided
        if (Start.HasValue && End.HasValue && Start.Value > End.Value)
        {
            ViewData["Error"] = "Start must be before or equal to End.";
            Volunteers = await _volunteerService.GetAll().ConfigureAwait(false);
            ReportRows = new List<VolunteerClockingVm>();
            return Page();
        }

        if (VolunteerId.HasValue)
            ReportRows = await _service.GetClockingReportForVolunteer(VolunteerId.Value, start, end).ConfigureAwait(false);
        else
            ReportRows = await _service.GetClockingReport(start, end).ConfigureAwait(false);

        Volunteers = await _volunteerService.GetAll().ConfigureAwait(false);

        return Page();
    }
}
