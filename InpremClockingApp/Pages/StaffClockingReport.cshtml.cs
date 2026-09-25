using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Helpers;
using InpremClockingApp.Services;
using InpremClockingApp.Models;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace InpremClockingApp.Pages;

public class StaffClockingReport : PageModel
{
    private readonly StaffClockingService _service;
    private readonly StaffService _staffService;
    public List<ClockingStaff> ReportRows { get; set; } = new List<ClockingStaff>();
    public IEnumerable<InpremClockingApp.Models.Staff>? Staffs { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? StaffId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? End { get; set; }

    public StaffClockingReport(StaffClockingService service, StaffService staffService)
    {
        _service = service;
        _staffService = staffService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // default last 30 days
        var start = Start ?? OrgClock.NowLocal().Date.AddDays(-30);
        var end = End ?? OrgClock.NowLocal().Date.AddDays(1).AddTicks(-1); // include full end day if only date provided

        ViewData["StaffId"] = StaffId;
        ViewData["Start"] = start.ToString("yyyy-MM-ddTHH:mm");
        ViewData["End"] = end.ToString("yyyy-MM-ddTHH:mm");

        // Validation: ensure start <= end when both provided
        if (Start.HasValue && End.HasValue && Start.Value > End.Value)
        {
            ViewData["Error"] = "Start must be before or equal to End.";
            Staffs = await _staffService.GetAll().ConfigureAwait(false);
            ReportRows = new List<ClockingStaff>();
            return Page();
        }

        if (StaffId.HasValue)
            ReportRows = await _service.GetClockingReportForStaff(StaffId.Value, start, end).ConfigureAwait(false);
        else
            ReportRows = await _service.GetClockingReport(start, end).ConfigureAwait(false);

        Staffs = await _staffService.GetAll().ConfigureAwait(false);

        return Page();
    }
}
