using InpremClockingApp.Helpers;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace InpremClockingApp.Controllers.Api;

// All callers (HoursWorked, VolunteerClockingReport, VolunteerReport) are AdminOnly pages. See
// ROLES.md "Known gaps" - this controller previously had no [Authorize] at all.
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/people")]
public class VolunteerReportsController : ControllerBase
{
    private readonly VolunteerClockingService _clocking;
    private readonly VolunteerService _volunteer;
    private readonly ITenantClock _tenantClock;
    private readonly ICurrentTenantProfile _tenantProfile;

    public VolunteerReportsController(VolunteerClockingService clocking, VolunteerService volunteer, ITenantClock tenantClock, ICurrentTenantProfile tenantProfile)
    {
        _clocking = clocking;
        _volunteer = volunteer;
        _tenantClock = tenantClock;
        _tenantProfile = tenantProfile;
    }

    [HttpGet("volunteer-hours")]
    public async Task<IActionResult> GetVolunteerHours([FromQuery] int id, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
    {
        if (id <= 0) return BadRequest(new { error = "Invalid id" });
        var s = start?.Date ?? _tenantClock.NowLocal().Date.AddDays(-30);
        var e = end?.Date.AddDays(1).AddTicks(-1)
                ?? _tenantClock.NowLocal().Date.AddDays(1).AddTicks(-1);
        var rows = await _clocking.GetClockingReportForVolunteer(id, s, e);
        // compute total hours from returned rows
        double totalClockedHours = 0;
        double totalBreakHours = 0;

        foreach (var vm in rows)
        {
            var item = vm.Clocking?.FirstOrDefault();

            if (item?.ClockInTime != null && item.ClockOutTime != null)
            {
                totalClockedHours +=
                    (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
            }

            if (item?.LeaveOnBreakTime != null && item.ReturnOnBreakTime != null)
            {
                totalBreakHours +=
                    (item.ReturnOnBreakTime.Value - item.LeaveOnBreakTime.Value).TotalHours;
            }
        }

        double actualHoursWorked = totalClockedHours - totalBreakHours;

        var clockedMinutes = (int)Math.Round(totalClockedHours * 60);
        var clockedHours = clockedMinutes / 60;
        var clockedRemainingMinutes = clockedMinutes % 60;

        var breakMinutes = (int)Math.Round(totalBreakHours * 60);
        var breakHours = breakMinutes / 60;
        var breakRemainingMinutes = breakMinutes % 60;

        var actualMinutes = (int)Math.Round(actualHoursWorked * 60);
        var actualHours = actualMinutes / 60;
        var actualRemainingMinutes = actualMinutes % 60;

        var totalClockedDisplay =
            $"{clockedHours:D2} hour(s) {clockedRemainingMinutes:D2} minutes";

        var totalBreakDisplay =
            $"{breakHours:D2} hour(s) {breakRemainingMinutes:D2} minutes";

        var actualWorkedDisplay =
            $"{actualHours:D2} hour(s) {actualRemainingMinutes:D2} minutes";

        return Ok(new
        {
            volunteerId = id,
            start = s.ToString("o"),
            end = e.ToString("o"),

            totalClockedHours = Math.Round(totalClockedHours, 2),
            totalBreakHours = Math.Round(totalBreakHours, 2),
            actualHoursWorked = Math.Round(actualHoursWorked, 2),

            totalClockedDisplay,
            totalBreakDisplay,
            actualWorkedDisplay
        });
    }

    [HttpGet("volunteer-hours-pdf")]
    public async Task<IActionResult> GetVolunteerHoursPdf(
    [FromQuery] int id,
    [FromQuery] DateTime? start,
    [FromQuery] DateTime? end)
    {
        if (id <= 0)
            return BadRequest(new { error = "Invalid id" });

        var s = start ?? _tenantClock.NowLocal().Date.AddDays(-30);
        var e = end ?? _tenantClock.NowLocal().Date.AddDays(1).AddTicks(-1);

        // Get volunteer
        var volunteer = await _volunteer.GetById(id);

        if (volunteer == null)
            return NotFound(new { error = "Volunteer not found" });

        var volunteerName = volunteer.FullName?.Trim();

        if (string.IsNullOrWhiteSpace(volunteerName))
            volunteerName = "Volunteer";

        // Get clocking records
        var rows = await _clocking.GetClockingReportForVolunteer(id, s, e);

        double totalClockedHours = 0;
        double totalBreakHours = 0;

        foreach (var vm in rows)
        {
            var item = vm.Clocking?.FirstOrDefault();

            if (item?.ClockInTime != null && item.ClockOutTime != null)
            {
                totalClockedHours +=
                    (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
            }

            if (item?.LeaveOnBreakTime != null && item.ReturnOnBreakTime != null)
            {
                totalBreakHours +=
                    (item.ReturnOnBreakTime.Value - item.LeaveOnBreakTime.Value).TotalHours;
            }
        }

        double actualHoursWorked = totalClockedHours - totalBreakHours;

        // Convert decimal hours to hours and minutes
        var clockedMinutes = (int)Math.Round(totalClockedHours * 60);
        var clockedHours = clockedMinutes / 60;
        var clockedRemainingMinutes = clockedMinutes % 60;

        var breakMinutes = (int)Math.Round(totalBreakHours * 60);
        var breakHours = breakMinutes / 60;
        var breakRemainingMinutes = breakMinutes % 60;

        var actualMinutes = (int)Math.Round(actualHoursWorked * 60);
        var actualHours = actualMinutes / 60;
        var actualRemainingMinutes = actualMinutes % 60;

        var totalClockedDisplay =
            $"{clockedHours:D2} hour(s) {clockedRemainingMinutes:D2} minutes";

        var totalBreakDisplay =
            $"{breakHours:D2} hour(s) {breakRemainingMinutes:D2} minutes";

        var actualWorkedDisplay =
            $"{actualHours:D2} hour(s) {actualRemainingMinutes:D2} minutes";

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);

                // HEADER
                page.Header()
                    .Column(header =>
                    {
                        // Space reserved for logo
                        header.Item()
                            .Height(55);

                        header.Item()
                            .AlignCenter()
                            .Text(_tenantProfile.Name)
                            .FontSize(18)
                            .Bold();

                        header.Item()
                            .PaddingTop(5)
                            .LineHorizontal(1);
                    });

                // MAIN CONTENT
                page.Content()
                    .PaddingTop(25)
                    .Column(column =>
                    {
                        column.Spacing(15);

                        column.Item()
                            .Text(text =>
                            {
                                text.Span("This is a confirmation of volunteer hours worked by ")
                                    .FontSize(11);

                                text.Span(volunteerName)
                                    .Bold()
                                    .FontSize(11);

                                text.Span($" at {_tenantProfile.Name}" + (string.IsNullOrWhiteSpace(_tenantProfile.Address) ? "." : $", {_tenantProfile.Address}."))
                                    .FontSize(11);
                            });

                        column.Item()
                            .PaddingTop(10)
                            .Text("Volunteer Hours Report")
                            .FontSize(15)
                            .Bold();

                        column.Item()
                            .Text($"Volunteer: {volunteerName}")
                            .FontSize(11);

                        column.Item()
                            .Text($"Period: {s:dd MMM yyyy} - {e:dd MMM yyyy}")
                            .FontSize(11);

                        column.Item()
                            .PaddingTop(10)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.5f);
                                    columns.RelativeColumn(2.2f);
                                    columns.RelativeColumn(2.2f);
                                    columns.RelativeColumn(2.2f);
                                    columns.RelativeColumn(2.2f);
                                    columns.RelativeColumn(1.2f);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().HeaderText("Full Name");
                                    header.Cell().HeaderText("ClockIn");
                                    header.Cell().HeaderText("ClockOut");
                                    header.Cell().HeaderText("Leave On Break");
                                    header.Cell().HeaderText("Return from Break");
                                    header.Cell().HeaderText("Working Hours");
                                });

                                if (rows.Count == 0)
                                {
                                    table.Cell().ColumnSpan(6).BodyText("No clocking records found for this period.");
                                }

                                foreach (var vm in rows)
                                {
                                    var item = vm.Clocking?.FirstOrDefault();
                                    if (item == null) continue;

                                    double workingHours = 0;
                                    if (item.ClockInTime.HasValue && item.ClockOutTime.HasValue)
                                    {
                                        workingHours = (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
                                        if (item.LeaveOnBreakTime.HasValue && item.ReturnOnBreakTime.HasValue)
                                        {
                                            workingHours -= (item.ReturnOnBreakTime.Value - item.LeaveOnBreakTime.Value).TotalHours;
                                        }
                                    }

                                    table.Cell().BodyText(volunteerName ?? "");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item.ClockInTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item.ClockOutTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item.LeaveOnBreakTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item.ReturnOnBreakTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().BodyText(FormatHours(workingHours));
                                }
                            });

                        column.Item()
                            .PaddingTop(15)
                            .Border(1)
                            .Padding(15)
                            .Column(summary =>
                            {
                                summary.Spacing(8);

                                summary.Item()
                                    .Text($"Total Hours Clocked: {totalClockedDisplay}")
                                    .FontSize(11);

                                summary.Item()
                                    .Text($"Total Hours for Break: {totalBreakDisplay}")
                                    .FontSize(11);

                                summary.Item()
                                    .PaddingTop(5)
                                    .Text($"Actual Hours Worked: {actualWorkedDisplay}")
                                    .FontSize(13)
                                    .Bold();
                            });
                    });

                // FOOTER
                page.Footer()
                    .AlignCenter()
                    .Column(footer =>
                    {
                        footer.Item()
                            .Text("Please contact us for further information.")
                            .FontSize(9);

                        footer.Item()
                            .PaddingTop(3)
                            .Text("Inprem Admin")
                            .FontSize(9)
                            .Bold();
                    });
            });
        }).GeneratePdf();

        return File(
            pdfBytes,
            "application/pdf",
            $"volunteer-{id}-hours.pdf");
    }

    [HttpGet("volunteer-clocking-report-pdf")]
    public async Task<IActionResult> GetVolunteerClockingReportPdf(
        [FromQuery] int? volunteerId,
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end)
    {
        var s = start ?? _tenantClock.NowLocal().Date.AddDays(-30);
        var e = end ?? _tenantClock.NowLocal().Date.AddDays(1).AddTicks(-1);

        string? volunteerName = null;
        if (volunteerId.HasValue && volunteerId.Value > 0)
        {
            var volunteer = await _volunteer.GetById(volunteerId.Value);
            volunteerName = volunteer?.FullName?.Trim();
        }

        var rows = volunteerId.HasValue && volunteerId.Value > 0
            ? await _clocking.GetClockingReportForVolunteer(volunteerId.Value, s, e)
            : await _clocking.GetClockingReport(s, e);

        var totalWorked = TimeSpan.Zero;
        foreach (var vm in rows)
        {
            var item = vm.Clocking?.FirstOrDefault();
            if (item?.WorkingHours != null)
                totalWorked += item.WorkingHours.Value;
        }

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);

                page.Header()
                    .Column(header =>
                    {
                        header.Item().Height(55);

                        header.Item()
                            .AlignCenter()
                            .Text(_tenantProfile.Name)
                            .FontSize(18)
                            .Bold();

                        header.Item().PaddingTop(5).LineHorizontal(1);
                    });

                page.Content()
                    .PaddingTop(25)
                    .Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Text("Volunteer Clocking Report").FontSize(15).Bold();

                        column.Item()
                            .Text(string.IsNullOrWhiteSpace(volunteerName) ? "Volunteer: All Volunteers" : $"Volunteer: {volunteerName}")
                            .FontSize(11);

                        column.Item().Text($"Period: {s:dd MMM yyyy} - {e:dd MMM yyyy}").FontSize(11);

                        column.Item()
                            .PaddingTop(10)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().HeaderText("Volunteer");
                                    header.Cell().HeaderText("Date");
                                    header.Cell().HeaderText("Clock In");
                                    header.Cell().HeaderText("Clock Out");
                                    header.Cell().HeaderText("Break Start");
                                    header.Cell().HeaderText("Break End");
                                    header.Cell().HeaderText("Working Hours");
                                });

                                foreach (var vm in rows)
                                {
                                    var item = vm.Clocking?.FirstOrDefault();
                                    table.Cell().BodyText(item?.FullName ?? "");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item?.CreatedAt)?.ToString("yyyy-MM-dd") ?? "");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item?.ClockInTime)?.ToString("HH:mm:ss") ?? "");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item?.ClockOutTime)?.ToString("HH:mm:ss") ?? "");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item?.LeaveOnBreakTime)?.ToString("HH:mm:ss") ?? "-");
                                    table.Cell().BodyText(_tenantClock.ToLocal(item?.ReturnOnBreakTime)?.ToString("HH:mm:ss") ?? "-");
                                    table.Cell().BodyText(item?.WorkingHours?.ToString(@"hh\:mm\:ss") ?? "");
                                }
                            });

                        column.Item()
                            .PaddingTop(10)
                            .Text($"Total Hours: {(int)totalWorked.TotalHours:D2}:{totalWorked.Minutes:D2}:{totalWorked.Seconds:D2}")
                            .FontSize(12)
                            .Bold();
                    });

                page.Footer()
                    .AlignCenter()
                    .Column(footer =>
                    {
                        footer.Item().Text("Please contact us for further information.").FontSize(9);
                        footer.Item().PaddingTop(3).Text("Inprem Admin").FontSize(9).Bold();
                    });
            });
        }).GeneratePdf();

        var fname = volunteerId.HasValue && volunteerId.Value > 0
            ? $"volunteer-{volunteerId}-clocking-report-{s:yyyyMMdd}-{e:yyyyMMdd}.pdf"
            : $"volunteer-clocking-report-{s:yyyyMMdd}-{e:yyyyMMdd}.pdf";

        return File(pdfBytes, "application/pdf", fname);
    }

    [HttpGet("volunteer-list-pdf")]
    public async Task<IActionResult> GetVolunteerListPdf()
    {
        var volunteerList = (await _volunteer.GetAll()).ToList();

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);

                page.Header()
                    .Column(header =>
                    {
                        header.Item().Height(55);

                        header.Item()
                            .AlignCenter()
                            .Text(_tenantProfile.Name)
                            .FontSize(18)
                            .Bold();

                        header.Item().PaddingTop(5).LineHorizontal(1);
                    });

                page.Content()
                    .PaddingTop(25)
                    .Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Text("Volunteer Report").FontSize(15).Bold();
                        column.Item().Text($"Total Volunteers: {volunteerList.Count}").FontSize(11);

                        column.Item()
                            .PaddingTop(10)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().HeaderText("ID");
                                    header.Cell().HeaderText("First Name");
                                    header.Cell().HeaderText("Last Name");
                                    header.Cell().HeaderText("Email");
                                    header.Cell().HeaderText("Gender");
                                    header.Cell().HeaderText("Phone");
                                    header.Cell().HeaderText("Zip");
                                    header.Cell().HeaderText("Category");
                                });

                                foreach (var volunteer in volunteerList)
                                {
                                    table.Cell().BodyText(volunteer.VolunteerId.ToString());
                                    table.Cell().BodyText(volunteer.FirstName ?? "");
                                    table.Cell().BodyText(volunteer.LastName ?? "");
                                    table.Cell().BodyText(volunteer.EmailAddress ?? "");
                                    table.Cell().BodyText(volunteer.Gender.ToString());
                                    table.Cell().BodyText(volunteer.PhoneNumber ?? "");
                                    table.Cell().BodyText(volunteer.ZipCode ?? "");
                                    table.Cell().BodyText(string.IsNullOrWhiteSpace(volunteer.VolunteerCategory) ? "-" : volunteer.VolunteerCategory);
                                }
                            });
                    });

                page.Footer()
                    .AlignCenter()
                    .Column(footer =>
                    {
                        footer.Item().Text("Please contact us for further information.").FontSize(9);
                        footer.Item().PaddingTop(3).Text("Inprem Admin").FontSize(9).Bold();
                    });
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", "volunteer-list.pdf");
    }

    // Delegates to the shared helper so the PDF's per-row Working Hours column reads the same way
    // as the on-screen table it's downloaded from.
    private static string FormatHours(double hours) => WorkingHoursFormat.ToHoursMinutes(hours);
}
