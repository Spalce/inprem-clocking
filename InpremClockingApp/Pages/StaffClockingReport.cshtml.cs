using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
    private readonly ITenantClock _tenantClock;
    public List<ClockingStaff> ReportRows { get; set; } = new List<ClockingStaff>();
    public IEnumerable<InpremClockingApp.Models.Staff>? Staffs { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? StaffId { get; set; }

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

    public StaffClockingReport(StaffClockingService service, StaffService staffService, ITenantClock tenantClock)
    {
        _service = service;
        _staffService = staffService;
        _tenantClock = tenantClock;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // default last 30 days
        var start = Start ?? _tenantClock.NowLocal().Date.AddDays(-30);
        var end = End ?? _tenantClock.NowLocal().Date.AddDays(1).AddTicks(-1); // include full end day if only date provided

        ViewData["StaffId"] = StaffId;
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
            Staffs = await _staffService.GetAll().ConfigureAwait(false);
            ReportRows = new List<ClockingStaff>();
            return Page();
        }

        var result = await _service.GetClockingReportPaged(start, end, StaffId, PageNumber, PageSize).ConfigureAwait(false);
        ReportRows = result.Items.ToList();

        ViewData["Page"] = result.Page;
        ViewData["PageSize"] = result.PageSize;
        ViewData["TotalCount"] = result.TotalCount;
        ViewData["TotalPages"] = result.TotalPages;

        Staffs = await _staffService.GetAll().ConfigureAwait(false);

        return Page();
    }
}
