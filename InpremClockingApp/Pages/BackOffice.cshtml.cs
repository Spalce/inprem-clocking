using InpremClockingApp.Data;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace InpremClockingApp.Pages
{
    public class BackOfficeModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<AppUser> _user;
        private readonly ITenantClock _tenantClock;
        private readonly ICurrentTenantProfile _tenantProfile;

        public BackOfficeModel(ApplicationDbContext db, UserManager<AppUser> user, ITenantClock tenantClock, ICurrentTenantProfile tenantProfile)
        {
            _db = db;
            _user = user;
            _tenantClock = tenantClock;
            _tenantProfile = tenantProfile;
        }

        public Dashboard? Dashboard  = new();
        public Dashboard? ThisWeek  = new();
        public Dashboard? Today  = new();
        public Dashboard? ThisMonth  = new();

        // Date-range "Calculate Total Hours" / "Download Report" pair, lives on the Volunteer
        // card only - matches the reference app exactly (it never offered this for Staff).
        // Default range is year-to-date (Jan 1 of the current year through today), not a rolling
        // window, again matching the reference.
        [BindProperty(SupportsGet = true)]
        public DateTime? RangeStart { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? RangeEnd { get; set; }

        public int PeriodVolunteerWorkedCount { get; set; }

        public async Task OnGet()
        {
            var staffs = await _db.Staffs.ToListAsync().ConfigureAwait(true);
            var volunteers = await _db.Volunteers.ToListAsync().ConfigureAwait(true);
            var staffClocking = await _db.ClockingsStaff.ToListAsync().ConfigureAwait(true);
            var users = await _user.Users.ToListAsync().ConfigureAwait(true);

            if (staffs != null!)
            {
                Dashboard!.StaffCount = staffs.Count;

            }

            if (volunteers != null!)
            {
                Dashboard!.VolunteerCount = volunteers.Count;
            }

            if (users != null!)
            {
                Dashboard!.AdminCount = users.Count;
            }

            if (staffClocking != null!)
            {
                var sumHours = staffClocking.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
                var sumMinutes = staffClocking.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
                Dashboard!.StaffClocking = $"{sumHours + sumMinutes / 60} Hours {sumMinutes % 60} Minutes";
            }

            var (start, end) = ResolveRange();
            ViewData["RangeStart"] = start.ToString("yyyy-MM-dd");
            ViewData["RangeEnd"] = end.ToString("yyyy-MM-dd");

            // "Working Time" on the Volunteer card is scoped to the selected period (recomputed
            // by "Calculate Total Hours" alongside the worked-count below), not an all-time
            // total - same date range the Download Report PDF uses.
            var periodVolunteerRecords = await GetVolunteerPeriodRecordsAsync(start, end).ConfigureAwait(true);
            PeriodVolunteerWorkedCount = periodVolunteerRecords.Select(e => e.VoluntId).Distinct().Count();

            var periodSumHours = periodVolunteerRecords.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
            var periodSumMinutes = periodVolunteerRecords.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
            Dashboard!.VolunteerClocking = $"{periodSumHours + periodSumMinutes / 60} Hours {periodSumMinutes % 60} Minutes";
        }

        public async Task<IActionResult> OnGetDownloadReportAsync()
        {
            var (start, end) = ResolveRange();
            var records = await GetVolunteerPeriodRecordsAsync(start, end).ConfigureAwait(true);

            var volunteerCount = records.Select(e => e.VoluntId).Distinct().Count();
            var totalHours = records.Sum(e => e.WorkingHours?.TotalHours ?? 0);

            var monthlyBreakdown = records
                .Select(e => new { Local = _tenantClock.ToLocal(e.ClockInTime), Hours = e.WorkingHours?.TotalHours ?? 0 })
                .Where(x => x.Local.HasValue)
                .GroupBy(x => new { x.Local!.Value.Year, x.Local.Value.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new
                {
                    g.Key.Year,
                    MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMMM"),
                    Hours = Math.Round(g.Sum(x => x.Hours), 2)
                })
                .ToList();

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(50);

                    page.Header()
                        .Column(header =>
                        {
                            // Space reserved for logo - see RosterExport/other reports for the
                            // same convention. No logo asset is wired up yet.
                            header.Item().Height(55);

                            header.Item().Text(_tenantProfile.Name).FontSize(12).Bold();
                            header.Item().PaddingTop(5).AlignCenter().Text("Volunteer Report").FontSize(20).Bold();
                        });

                    page.Content()
                        .PaddingTop(20)
                        .Column(column =>
                        {
                            column.Spacing(10);

                            column.Item().Text($"Total number of Volunteers that clocked in for the selected period: {volunteerCount}").FontSize(11);
                            column.Item().Text($"Total hours worked by volunteers for the selected period: {Math.Round(totalHours, 2)} hours").FontSize(11);
                            column.Item().Text("Monthly breakdown of total hours worked:").FontSize(11);

                            column.Item()
                                .PaddingTop(5)
                                .Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(1);
                                        columns.RelativeColumn(2);
                                        columns.RelativeColumn(2);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("Year").FontColor(Colors.White).Bold();
                                        header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("Month").FontColor(Colors.White).Bold();
                                        header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("Total Hours").FontColor(Colors.White).Bold();
                                    });

                                    foreach (var row in monthlyBreakdown)
                                    {
                                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(row.Year.ToString());
                                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(row.MonthName);
                                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(row.Hours.ToString());
                                    }
                                });
                        });

                    page.Footer()
                        .Column(footer =>
                        {
                            footer.Item().Text("Please contact us for further information.").FontSize(10);
                            footer.Item().PaddingTop(3).Text("Inprem Admin").FontSize(10);
                        });
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf", "Volunteer_Report.pdf");
        }

        private (DateTime start, DateTime end) ResolveRange()
        {
            var now = _tenantClock.NowLocal();
            var start = RangeStart ?? new DateTime(now.Year, 1, 1);
            var end = RangeEnd ?? now.Date.AddDays(1).AddTicks(-1);
            return (start, end);
        }

        private async Task<List<Clocking>> GetVolunteerPeriodRecordsAsync(DateTime start, DateTime end)
        {
            var startUtc = _tenantClock.ToUtc(start);
            var endUtc = _tenantClock.ToUtc(end);

            return await _db.Clockings
                .Where(e => (e.ClockInTime >= startUtc && e.ClockInTime <= endUtc)
                            || (e.ClockOutTime != null && e.ClockOutTime >= startUtc && e.ClockOutTime <= endUtc))
                .ToListAsync().ConfigureAwait(false);
        }
    }
}
