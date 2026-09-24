using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace InpremClockingApp.Controllers.Api;

[ApiController]
[Route("api/people")]
public class StaffReportsController : ControllerBase
{
    private readonly StaffClockingService _clocking;
    private readonly StaffService _staff;

    public StaffReportsController(StaffClockingService clocking, StaffService staff)
    {
        _clocking = clocking;
        _staff = staff;
    }

    [HttpGet("staff-hours")]
    public async Task<IActionResult> GetStaffHours([FromQuery] int id, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
    {
        if (id <= 0) return BadRequest(new { error = "Invalid id" });
        var s = start ?? DateTime.Now.Date.AddDays(-30);
        var e = end ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);
        var rows = await _clocking.GetClockingReportForStaff(id, s, e);
        // compute total hours from returned rows
        double totalHours = 0;
        foreach (var vm in rows)
        {
            var item = vm;
            if (item?.WorkingHours != null)
            {
                totalHours += item.WorkingHours.Value.TotalHours;
            }
            else if (item?.ClockInTime != null && item?.ClockOutTime != null)
            {
                totalHours += (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
            }
        }

        var totalMinutes = (int)Math.Round(totalHours * 60);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        var totalDisplay = $"{hours:D2} hour(s) {minutes:D2} minutes";

        return Ok(new
        {
            staffId = id,
            start = s.ToString("o"),
            end = e.ToString("o"),
            totalHours = Math.Round(totalHours, 2),
            totalDisplay
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

        var s = start ?? DateTime.Now.Date.AddDays(-30);
        var e = end ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

        // Get staff
        var staff = await _staff.GetById(id);

        if (staff == null)
            return NotFound(new { error = "Staff not found" });

        var staffName = staff.FullName?.Trim();

        if (string.IsNullOrWhiteSpace(staffName))
            staffName = "Staff";

        // Get clocking records
        var rows = await _clocking.GetClockingReportForStaff(id, s, e);

        double totalHours = 0;

        foreach (var vm in rows)
        {
            var item = vm;

            if (item?.WorkingHours != null)
            {
                totalHours += item.WorkingHours.Value.TotalHours;
            }
            else if (item?.ClockInTime != null && item?.ClockOutTime != null)
            {
                totalHours +=
                    (item.ClockOutTime.Value - item.ClockInTime.Value).TotalHours;
            }
        }

        // Convert decimal hours to hours and minutes
        var totalMinutes = (int)Math.Round(totalHours * 60);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        var totalDisplay = $"{hours:D2} hour(s) {minutes:D2} minutes";

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
                            .Text("Inprem Holistic Community Resource Center")
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

                                text.Span(
                                    " at Inprem Holistic Community Resource Center, " +
                                    "5757 Karl Rd, Columbus Ohio.")
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
                            .PaddingTop(15)
                            .Border(1)
                            .Padding(15)
                            .AlignCenter()
                            .Text($"Total : {totalDisplay}")
                            .FontSize(16)
                            .Bold();
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

}
