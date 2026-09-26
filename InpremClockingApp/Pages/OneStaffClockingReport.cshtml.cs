using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Helpers;
using InpremClockingApp.Services;
using InpremClockingApp.Models;
using System.Threading.Tasks;

namespace InpremClockingApp.Pages;

public class OneStaffClockingReport : PageModel
{
    private readonly StaffClockingService _service;
    public List<ClockingStaff> ReportRows { get; set; } = new List<ClockingStaff>();

    public OneStaffClockingReport(StaffClockingService service)
    {
        _service = service;
    }

    public async Task OnGetAsync(int staffId)
    {
        var start = OrgClock.NowLocal().Date.AddDays(-30);
        var end = OrgClock.NowLocal().Date;
        ReportRows = await _service.GetClockingReportForStaff(staffId, start, end).ConfigureAwait(false);
    }
}
