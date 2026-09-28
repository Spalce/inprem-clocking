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

    // Named "pageNumber" rather than "page" because "page" is a reserved Razor Pages route
    // value (the page's own path) - a query string "page" is intercepted by route-value model
    // binding before it ever reaches a same-named property.
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public VolunteerClockingReport(VolunteerClockingService service, VolunteerService volunteerService)
    {
        _service = service;
        _volunteerService = volunteerService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var start = Start ?? OrgClock.NowLocal().Date.AddDays(-30);
        var end = End ?? OrgClock.NowLocal().Date.AddDays(1).AddTicks(-1);

        ViewData["VolunteerId"] = VolunteerId;
        ViewData["Start"] = start.ToString("yyyy-MM-ddTHH:mm");
        ViewData["End"] = end.ToString("yyyy-MM-ddTHH:mm");

        // Validation: ensure start <= end when both provided
        if (Start.HasValue && End.HasValue && Start.Value > End.Value)
        {
            ViewData["Error"] = "Start must be before or equal to End.";
            ViewData["Page"] = 1;
            ViewData["PageSize"] = PageSize;
            ViewData["TotalCount"] = 0;
            ViewData["TotalPages"] = 0;
            Volunteers = await _volunteerService.GetAll().ConfigureAwait(false);
            ReportRows = new List<VolunteerClockingVm>();
            return Page();
        }

        var result = await _service.GetClockingReportPaged(start, end, VolunteerId, PageNumber, PageSize).ConfigureAwait(false);
        ReportRows = result.Items.ToList();

        ViewData["Page"] = result.Page;
        ViewData["PageSize"] = result.PageSize;
        ViewData["TotalCount"] = result.TotalCount;
        ViewData["TotalPages"] = result.TotalPages;

        Volunteers = await _volunteerService.GetAll().ConfigureAwait(false);

        return Page();
    }
}
