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
    public List<ClockingStaff> ReportRows { get; set; } = new List<ClockingStaff>();

    public StaffClockingReport(StaffClockingService service)
    {
        _service = service;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Load report rows from service (default last 30 days)
        var start = DateTime.Now.Date.AddDays(-30);
        var end = DateTime.Now.Date;
        ReportRows = await _service.GetClockingReport(start, end).ConfigureAwait(false);
        return Page();
    }
}
