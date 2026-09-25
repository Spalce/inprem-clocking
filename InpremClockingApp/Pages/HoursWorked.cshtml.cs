using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class HoursWorkedModel : PageModel
{
    private readonly VolunteerClockingService _volunteerClockingService;
    private readonly VolunteerService _volunteerService;
    private readonly StaffClockingService _staffClockingService;
    private readonly StaffService _staffService;

    public HoursWorkedModel(
        VolunteerClockingService volunteerClockingService,
        VolunteerService volunteerService,
        StaffClockingService staffClockingService,
        StaffService staffService)
    {
        _volunteerClockingService = volunteerClockingService;
        _volunteerService = volunteerService;
        _staffClockingService = staffClockingService;
        _staffService = staffService;
    }

    public Models.Volunteer? Volunteer { get; set; }
    public Models.Staff? Staff { get; set; }

    public PagedResult<Clocking>? ClockingRecords { get; set; }
    public PagedResult<ClockingStaff>? StaffClockingRecords { get; set; }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "volunteer";

    [BindProperty(SupportsGet = true)]
    public DateTime? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? End { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public double TotalClockedHours { get; set; }
    public double TotalBreakHours { get; set; }
    public double ActualHoursWorked { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id <= 0)
            return NotFound();

        if (Page < 1)
            Page = 1;

        if (PageSize != 10 &&
            PageSize != 20 &&
            PageSize != 50 &&
            PageSize != 100)
        {
            PageSize = 20;
        }

        var startDate = Start?.Date;
        var endDate = End?.Date.AddDays(1).AddTicks(-1);

        var reportStart = startDate ?? DateTime.MinValue;
        var reportEnd = endDate ?? DateTime.MaxValue;

        if (string.Equals(Type, "volunteer", StringComparison.OrdinalIgnoreCase))
        {
            Volunteer = await _volunteerService.GetById(Id);

            if (Volunteer == null)
                return NotFound();

            ClockingRecords = await _volunteerClockingService.GetPaged(
                Page,
                PageSize,
                (int)Id,
                startDate,
                endDate);

            var allRecords = await _volunteerClockingService
                .GetClockingReportForVolunteer(
                    (int)Id,
                    reportStart,
                    reportEnd);

            foreach (var vm in allRecords)
            {
                var item = vm.Clocking?.FirstOrDefault();

                if (item?.ClockInTime != null && item.ClockOutTime != null)
                {
                    TotalClockedHours +=
                        (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
                }

                if (item?.LeaveOnBreakTime != null &&
                    item.ReturnOnBreakTime != null)
                {
                    TotalBreakHours +=
                        (item.ReturnOnBreakTime.Value -
                         item.LeaveOnBreakTime.Value).TotalHours;
                }
            }
        }
        else if (string.Equals(Type, "staff", StringComparison.OrdinalIgnoreCase))
        {
            Staff = await _staffService.GetById(Id);

            if (Staff == null)
                return NotFound();

            StaffClockingRecords = await _staffClockingService.GetPaged(
                Page,
                PageSize,
                (int)Id,
                startDate,
                endDate);

            var allRecords = await _staffClockingService
                .GetClockingReportForStaff(
                    (int)Id,
                    reportStart,
                    reportEnd);

            foreach (var item in allRecords)
            {
                if (item.ClockInTime != null && item.ClockOutTime != null)
                {
                    TotalClockedHours +=
                        (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
                }

                if (item.LeaveOnBreakTime != null &&
                    item.ReturnOnBreakTime != null)
                {
                    TotalBreakHours +=
                        (item.ReturnOnBreakTime.Value -
                         item.LeaveOnBreakTime.Value).TotalHours;
                }
            }
        }
        else
        {
            return BadRequest();
        }

        ActualHoursWorked = TotalClockedHours - TotalBreakHours;

        return Page();
    }

    public string FormatHours(double hours)
    {
        var totalMinutes = (int)Math.Round(hours * 60);

        var wholeHours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        return $"{wholeHours:D2}:{minutes:D2}";
    }

    public string FormatDateTime(DateTime? value)
    {
        return OrgClock.ToLocal(value)?.ToString("dd/MM/yyyy HH:mm") ?? "-";
    }
}