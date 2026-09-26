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

    public int StaffId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public OneStaffClockingReport(StaffClockingService service)
    {
        _service = service;
    }

    public async Task OnGetAsync(int staffId)
    {
        StaffId = staffId;
        Start = OrgClock.NowLocal().Date.AddDays(-30);
        End = OrgClock.NowLocal().Date;
        ReportRows = await _service.GetClockingReportForStaff(staffId, Start, End).ConfigureAwait(false);
    }
}
