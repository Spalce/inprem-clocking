using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace InpremClockingApp.Controllers.Api;

// All callers (HoursWorked, StaffClockingReport, StaffReport) are AdminOnly pages. See
// ROLES.md "Known gaps" - this controller previously had no [Authorize] at all.
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/people")]
public class StaffReportsController : ControllerBase
{
    private readonly StaffClockingService _clocking;
    private readonly StaffService _staff;
    private readonly ITenantClock _tenantClock;
    private readonly ICurrentTenantProfile _tenantProfile;

    public StaffReportsController(StaffClockingService clocking, StaffService staff, ITenantClock tenantClock, ICurrentTenantProfile tenantProfile)
    {
        _clocking = clocking;
        _staff = staff;
        _tenantClock = tenantClock;
        _tenantProfile = tenantProfile;
    }

    [HttpGet("staff-hours")]
    public async Task<IActionResult> GetStaffHours([FromQuery] int id, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
    {
        if (id <= 0) return BadRequest(new { error = "Invalid id" });
        var s = start?.Date ?? _tenantClock.NowLocal().Date.AddDays(-30);
        var e = end?.Date.AddDays(1).AddTicks(-1)
                ?? _tenantClock.NowLocal().Date.AddDays(1).AddTicks(-1);
        var rows = await _clocking.GetClockingReportForStaff(id, s, e);
        // compute total hours from returned rows
        double totalClockedHours = 0;
        double totalBreakHours = 0;

        foreach (var item in rows)
        {
            if (item.ClockInTime != null && item.ClockOutTime != null)
            {
                totalClockedHours +=
                    (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
            }

            if (item.LeaveOnBreakTime != null && item.ReturnOnBreakTime != null)
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

        return Ok(new
        {
            staffId = id,
            start = s.ToString("o"),
            end = e.ToString("o"),

            totalClockedHours = Math.Round(totalClockedHours, 2),
            totalBreakHours = Math.Round(totalBreakHours, 2),
            actualHoursWorked = Math.Round(actualHoursWorked, 2),

            totalClockedDisplay = $"{clockedHours:D2} hour(s) {clockedRemainingMinutes:D2} minutes",
            totalBreakDisplay = $"{breakHours:D2} hour(s) {breakRemainingMinutes:D2} minutes",
            actualWorkedDisplay = $"{actualHours:D2} hour(s) {actualRemainingMinutes:D2} minutes"
        });              
    }

    [HttpGet("staff-hours-pdf")]
    public async Task<IActionResult> GetStaffHoursPdf(
    [FromQuery] int id,
    [FromQuery] DateTime? start,
    [FromQuery] DateTime? end)
    {
        if (id <= 0)
            return BadRequest(new { error = "Invalid id" });

        var s = start ?? _tenantClock.NowLocal().Date.AddDays(-30);
        var e = end ?? _tenantClock.NowLocal().Date.AddDays(1).AddTicks(-1);

        // Get staff
        var staff = await _staff.GetById(id);

        if (staff == null)
            return NotFound(new { error = "Staff not found" });

        var staffName = staff.FullName?.Trim();

        if (string.IsNullOrWhiteSpace(staffName))
            staffName = "Staff";

        // Get clocking records
        var rows = await _clocking.GetClockingReportForStaff(id, s, e);

        double totalClockedHours = 0;
        double totalBreakHours = 0;

        foreach (var item in rows)
        {
            if (item.ClockInTime != null && item.ClockOutTime != null)
            {
                totalClockedHours +=
                    (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
            }

            if (item.LeaveOnBreakTime != null && item.ReturnOnBreakTime != null)
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
                                text.Span("This is a confirmation of staff hours worked by ")
                                    .FontSize(11);

                                text.Span(staffName)
                                    .Bold()
                                    .FontSize(11);

                                text.Span($" at {_tenantProfile.Name}" + (string.IsNullOrWhiteSpace(_tenantProfile.Address) ? "." : $", {_tenantProfile.Address}."))
                                    .FontSize(11);
                            });

                        column.Item()
                            .PaddingTop(10)
                            .Text("Staff Hours Report")
                            .FontSize(15)
                            .Bold();

                        column.Item()
                            .Text($"Staff: {staffName}")
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
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("Full Name").FontColor(Colors.White).Bold();
                                    header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("ClockIn").FontColor(Colors.White).Bold();
                                    header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("ClockOut").FontColor(Colors.White).Bold();
                                    header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("Leave On Break").FontColor(Colors.White).Bold();
                                    header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("Return from Break").FontColor(Colors.White).Bold();
                                    header.Cell().Background(Colors.BlueGrey.Darken4).Padding(5).AlignCenter().Text("Working Hours").FontColor(Colors.White).Bold();
                                });

                                if (rows.Count == 0)
                                {
                                    table.Cell().ColumnSpan(6).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text("No clocking records found for this period.");
                                }

                                foreach (var item in rows)
                                {
                                    double workingHours = 0;
                                    if (item.ClockInTime.HasValue && item.ClockOutTime.HasValue)
                                    {
                                        workingHours = (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
                                        if (item.LeaveOnBreakTime.HasValue && item.ReturnOnBreakTime.HasValue)
                                        {
                                            workingHours -= (item.ReturnOnBreakTime.Value - item.LeaveOnBreakTime.Value).TotalHours;
                                        }
                                    }

                                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(staffName);
                                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(_tenantClock.ToLocal(item.ClockInTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(_tenantClock.ToLocal(item.ClockOutTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(_tenantClock.ToLocal(item.LeaveOnBreakTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(_tenantClock.ToLocal(item.ReturnOnBreakTime)?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(FormatHours(workingHours));
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
            $"staff-{id}-hours.pdf");
    }

    [HttpGet("staff-clocking-report-pdf")]
    public async Task<IActionResult> GetStaffClockingReportPdf(
        [FromQuery] int? staffId,
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end)
    {
        var s = start ?? _tenantClock.NowLocal().Date.AddDays(-30);
        var e = end ?? _tenantClock.NowLocal().Date.AddDays(1).AddTicks(-1);

        string? staffName = null;
        if (staffId.HasValue && staffId.Value > 0)
        {
            var staff = await _staff.GetById(staffId.Value);
            staffName = staff?.FullName?.Trim();
        }

        var rows = staffId.HasValue && staffId.Value > 0
            ? await _clocking.GetClockingReportForStaff(staffId.Value, s, e)
            : await _clocking.GetClockingReport(s, e);

        var totalWorked = TimeSpan.Zero;
        foreach (var row in rows)
        {
            if (row.WorkingHours.HasValue)
                totalWorked += row.WorkingHours.Value;
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

                        column.Item().Text("Staff Clocking Report").FontSize(15).Bold();

                        column.Item()
                            .Text(string.IsNullOrWhiteSpace(staffName) ? "Staff: All Staff" : $"Staff: {staffName}")
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
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("Staff").Bold();
                                    header.Cell().Text("Date").Bold();
                                    header.Cell().Text("Clock In").Bold();
                                    header.Cell().Text("Clock Out").Bold();
                                    header.Cell().Text("Hours").Bold();
                                });

                                foreach (var row in rows)
                                {
                                    table.Cell().Text(row.FullName ?? "");
                                    table.Cell().Text(_tenantClock.ToLocal(row.CreatedAt)?.ToString("yyyy-MM-dd") ?? "");
                                    table.Cell().Text(_tenantClock.ToLocal(row.ClockInTime)?.ToString("HH:mm:ss") ?? "");
                                    table.Cell().Text(_tenantClock.ToLocal(row.ClockOutTime)?.ToString("HH:mm:ss") ?? "");
                                    table.Cell().Text(row.WorkingHours?.ToString(@"hh\:mm\:ss") ?? "");
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

        var fname = staffId.HasValue && staffId.Value > 0
            ? $"staff-{staffId}-clocking-report-{s:yyyyMMdd}-{e:yyyyMMdd}.pdf"
            : $"staff-clocking-report-{s:yyyyMMdd}-{e:yyyyMMdd}.pdf";

        return File(pdfBytes, "application/pdf", fname);
    }

    [HttpGet("staff-list-pdf")]
    public async Task<IActionResult> GetStaffListPdf()
    {
        var staffList = (await _staff.GetAll()).ToList();

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

                        column.Item().Text("Staff Report").FontSize(15).Bold();
                        column.Item().Text($"Total Staff: {staffList.Count}").FontSize(11);

                        column.Item()
                            .PaddingTop(10)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(2);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("First Name").Bold();
                                    header.Cell().Text("Last Name").Bold();
                                    header.Cell().Text("Email").Bold();
                                    header.Cell().Text("Phone").Bold();
                                });

                                foreach (var staff in staffList)
                                {
                                    table.Cell().Text(staff.FirstName ?? "");
                                    table.Cell().Text(staff.LastName ?? "");
                                    table.Cell().Text(staff.EmailAddress ?? "");
                                    table.Cell().Text(staff.PhoneNumber ?? "");
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

        return File(pdfBytes, "application/pdf", "staff-list.pdf");
    }

    // Matches HoursWorked.cshtml.cs's own FormatHours exactly, so the PDF's per-row Working
    // Hours column reads the same way as the on-screen table it's downloaded from.
    private static string FormatHours(double hours)
    {
        var totalMinutes = (int)Math.Round(hours * 60);
        var wholeHours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{wholeHours:D2}:{minutes:D2}";
    }
}
