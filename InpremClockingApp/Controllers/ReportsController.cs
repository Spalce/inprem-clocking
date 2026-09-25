
using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;

    public ReportsController(ApplicationDbContext db)
    {
        _db = db;
    }

    // model.StartDate/EndDate are org-local calendar-day boundaries picked by the user;
    // convert to UTC before querying since CreatedAt/clock times are stored in UTC.
    private static (DateTime? StartUtc, DateTime? EndUtcExclusive) LocalRangeToUtc(ReportModel model)
    {
        var startUtc = OrgClock.ToUtc(model.StartDate?.Date);
        var endUtc = OrgClock.ToUtc(model.EndDate?.Date.AddDays(1));
        return (startUtc, endUtc);
    }

    [Produces("application/json")]
    [HttpPost("staff")]
    public async Task<ActionResult<ReportDataSet<Content>>> GetStaff([FromBody] ReportModel model)
    {
        try
        {
            var (startUtc, endUtc) = LocalRangeToUtc(model);
            var record = await _db.Staffs.Where(e =>
                    e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Content
                {
                    Name = e.FullName,
                    Phone = e.PhoneNumber,
                    Email = e.EmailAddress,
                    Gender = e.Gender.ToString(),
                    Date = OrgClock.ToLocal(e.CreatedAt)?.ToString("dd-MM-yyyy"),
                    Zip = e.ZipCode,
                    Address = e.Address
                }).ToList();

                return Ok(new ReportDataSet<Content>
                {
                    Success = true,
                    Detail = new Detail
                    {
                        Company = "Inprem Holistic Community Resource Center",
                        Address = "5757 Karl Road, Columbus, OH 43229",
                        Contact = "614-516-1812 | Inpremcommunitycenter@yahoo.com",
                        Duration = $"{model.StartDate!.Value.Date:dd-MM-yyyy} To {model.EndDate!.Value.Date:dd-MM-yyyy}"
                    },
                    Contents = list
                });
            }

            return Ok(new ReportDataSet<Content>
            {
                Success = true,
                Detail = null,
                Contents = null
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(ModelState);
    }

    [Produces("application/json")]
    [HttpPost("volunteer")]
    public async Task<ActionResult<Content>> GetVolunteer([FromBody] ReportModel model)
    {
        try
        {
            var (startUtc, endUtc) = LocalRangeToUtc(model);
            var record = await _db.Volunteers.Where(e =>
                    e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Content
                {
                    Name = e.FullName,
                    Phone = e.PhoneNumber,
                    Email = e.EmailAddress,
                    Gender = e.Gender.ToString(),
                    Date = OrgClock.ToLocal(e.CreatedAt)?.ToString("dd-MM-yyyy")
                }).ToList();

                return Ok(new ReportDataSet<Content>
                {
                    Success = true,
                    Detail = new Detail
                    {
                        Company = "Inprem Holistic Community Resource Center",
                        Address = "5757 Karl Road, Columbus, OH 43229",
                        Contact = "614-516-1812 | Inpremcommunitycenter@yahoo.com",
                        Duration = $"{model.StartDate!.Value.Date:dd-MM-yyyy} To {model.EndDate!.Value.Date:dd-MM-yyyy}"
                    },
                    Contents = list
                });
            }

            return Ok(new ReportDataSet<Content>
            {
                Success = true,
                Detail = null,
                Contents = null
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(ModelState!);
    }

    [Produces("application/json")]
    [HttpPost("staff-clocking")]
    public async Task<ActionResult<ReportDataSet<Clockings>>> StaffClocking([FromBody] ReportModel model)
    {
        try
        {
            var (startUtc, endUtc) = LocalRangeToUtc(model);
            var record = await _db.ClockingsStaff
                .Where(e => e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = OrgClock.ToLocal(e.CreatedAt)?.ToString("dd-MM-yyyy"),
                    ClockIn = OrgClock.ToLocal(e.ClockInTime)?.ToString("HH:mm:ss"),
                    ClockOut = OrgClock.ToLocal(e.ClockOutTime)?.ToString("HH:mm:ss"),
                    BreakStart = OrgClock.ToLocal(e.LeaveOnBreakTime)?.ToString("HH:mm:ss"),
                    BreakEnd = OrgClock.ToLocal(e.ReturnOnBreakTime)?.ToString("HH:mm:ss"),
                    Hours = e.WorkingHours != null ? $"{e.WorkingHours!.Value.Hours} hours {e.WorkingHours.Value.Minutes} minutes" : null
                }).ToList();

                var sumHours = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
                var sumMinutes = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
                var total = $"{sumHours + sumMinutes / 60} Hours {sumMinutes % 60} Minutes";

                return Ok(new ReportDataSet<Clockings>
                {
                    Success = true,
                    Detail = new Detail
                    {
                        Company = "Inprem Holistic Community Resource Center",
                        Address = "5757 Karl Road, Columbus, OH 43229",
                        Contact = "614-516-1812 | Inpremcommunitycenter@yahoo.com",
                        Duration = total
                    },
                    Contents = list
                });
            }

            return Ok(new ReportDataSet<Clockings>
            {
                Success = true,
                Detail = null,
                Contents = null
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(ModelState);
    }

    [Produces("application/json")]
    [HttpPost("staff-clocking-one")]
    public async Task<ActionResult<ReportDataSet<Clockings>>> StaffClockingOne([FromBody] ReportModel model)
    {
        try
        {
            var (startUtc, endUtc) = LocalRangeToUtc(model);
            var record = await _db.ClockingsStaff
                .Where(e => e.StafId == model.Id && e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = OrgClock.ToLocal(e.CreatedAt)?.ToString("dd-MM-yyyy"),
                    ClockIn = OrgClock.ToLocal(e.ClockInTime)?.ToString("HH:mm:ss"),
                    ClockOut = OrgClock.ToLocal(e.ClockOutTime)?.ToString("HH:mm:ss"),
                    BreakStart = OrgClock.ToLocal(e.LeaveOnBreakTime)?.ToString("HH:mm:ss"),
                    BreakEnd = OrgClock.ToLocal(e.ReturnOnBreakTime)?.ToString("HH:mm:ss"),
                    Hours = e.WorkingHours != null ? $"{e.WorkingHours!.Value.Hours} hours {e.WorkingHours.Value.Minutes} minutes" : null
                }).ToList();

                var sumHours = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
                var sumMinutes = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
                var total = $"{sumHours + sumMinutes / 60} Hours {sumMinutes % 60} Minutes";

                return Ok(new ReportDataSet<Clockings>
                {
                    Success = true,
                    Detail = new Detail
                    {
                        Company = "Inprem Holistic Community Resource Center",
                        Address = "5757 Karl Road, Columbus, OH 43229",
                        Contact = "614-516-1812 | Inpremcommunitycenter@yahoo.com",
                        Duration = total
                    },
                    Contents = list
                });
            }

            return Ok(new ReportDataSet<Clockings>
            {
                Success = true,
                Detail = null,
                Contents = null
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(ModelState);
    }

    [Produces("application/json")]
    [HttpPost("volunteer-clocking")]
    public async Task<ActionResult> VolunteerClocking([FromBody] ReportModel model)
    {
        try
        {
            var (startUtc, endUtc) = LocalRangeToUtc(model);
            var record = await _db.Clockings.Where(e =>
                    e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = OrgClock.ToLocal(e.CreatedAt)?.ToString("dd-MM-yyyy"),
                    ClockIn = OrgClock.ToLocal(e.ClockInTime)?.ToString("HH:mm:ss"),
                    ClockOut = OrgClock.ToLocal(e.ClockOutTime)?.ToString("HH:mm:ss"),
                    BreakStart = OrgClock.ToLocal(e.LeaveOnBreakTime)?.ToString("HH:mm:ss"),
                    BreakEnd = OrgClock.ToLocal(e.ReturnOnBreakTime)?.ToString("HH:mm:ss"),
                    Hours = e.WorkingHours != null ? $"{e.WorkingHours!.Value.Hours} hours {e.WorkingHours.Value.Minutes} minutes" : null
                }).ToList();

                var sumHours = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
                var sumMinutes = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
                var total = $"{sumHours + sumMinutes / 60} Hours {sumMinutes % 60} Minutes";

                return Ok(new ReportDataSet<Clockings>
                {
                    Success = true,
                    Detail = new Detail
                    {
                        Company = "Inprem Holistic Community Resource Center",
                        Address = "5757 Karl Road, Columbus, OH 43229",
                        Contact = "614-516-1812 | Inpremcommunitycenter@yahoo.com",
                        Duration = total
                    },
                    Contents = list
                });
            }

            return Ok(new ReportDataSet<Clockings>
            {
                Success = true,
                Detail = null,
                Contents = null
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false!);
    }

    // Export endpoints: CSV (implemented) and PDF (not implemented server-side here)
    [HttpPost("export/staff-clocking")]
    public async Task<IActionResult> ExportStaffClocking([FromBody] ReportModel model, [FromQuery] string format = "csv")
    {
        var (startUtc, endUtc) = LocalRangeToUtc(model);
        var record = await _db.ClockingsStaff
            .Where(e => e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
            .ToListAsync();

        if (format?.ToLower() == "csv")
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Name,Date,ClockIn,ClockOut,BreakStart,BreakEnd,Hours");
            foreach (var e in record)
            {
                var line = string.Join(",",
                    EscapeCsv(e.FullName),
                    OrgClock.ToLocal(e.CreatedAt)?.ToString("yyyy-MM-dd"),
                    OrgClock.ToLocal(e.ClockInTime)?.ToString("HH:mm:ss"),
                    OrgClock.ToLocal(e.ClockOutTime)?.ToString("HH:mm:ss"),
                    OrgClock.ToLocal(e.LeaveOnBreakTime)?.ToString("HH:mm:ss"),
                    OrgClock.ToLocal(e.ReturnOnBreakTime)?.ToString("HH:mm:ss"),
                    e.WorkingHours != null ? e.WorkingHours.Value.ToString() : "");
                sb.AppendLine(line);
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var fname = $"staff_clocking_{model.StartDate:yyyyMMdd}_{model.EndDate:yyyyMMdd}.csv";
            return File(bytes, "text/csv", fname);
        }

        // PDF generation is not implemented here. Recommend using QuestPDF or another PDF library.
        return StatusCode(501, "PDF export not implemented. Add a PDF library (e.g., QuestPDF) to generate PDFs.");
    }

    [HttpPost("export/volunteer-clocking")]
    public async Task<IActionResult> ExportVolunteerClocking([FromBody] ReportModel model, [FromQuery] string format = "csv")
    {
        var (startUtc, endUtc) = LocalRangeToUtc(model);
        var record = await _db.Clockings
            .Where(e => e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
            .ToListAsync();

        if (format?.ToLower() == "csv")
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Name,Date,ClockIn,ClockOut,BreakStart,BreakEnd,Hours");
            foreach (var e in record)
            {
                var line = string.Join(",",
                    EscapeCsv(e.FullName),
                    OrgClock.ToLocal(e.CreatedAt)?.ToString("yyyy-MM-dd"),
                    OrgClock.ToLocal(e.ClockInTime)?.ToString("HH:mm:ss"),
                    OrgClock.ToLocal(e.ClockOutTime)?.ToString("HH:mm:ss"),
                    OrgClock.ToLocal(e.LeaveOnBreakTime)?.ToString("HH:mm:ss"),
                    OrgClock.ToLocal(e.ReturnOnBreakTime)?.ToString("HH:mm:ss"),
                    e.WorkingHours != null ? e.WorkingHours.Value.ToString() : "");
                sb.AppendLine(line);
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var fname = $"volunteer_clocking_{model.StartDate:yyyyMMdd}_{model.EndDate:yyyyMMdd}.csv";
            return File(bytes, "text/csv", fname);
        }

        return StatusCode(501, "PDF export not implemented. Add a PDF library (e.g., QuestPDF) to generate PDFs.");
    }

    private static string EscapeCsv(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        if (input.Contains(',') || input.Contains('"') || input.Contains('\n'))
        {
            return '"' + input.Replace("\"", "\"\"") + '"';
        }
        return input;
    }

    [Produces("application/json")]
    [HttpPost("volunteer-clocking-one")]
    public async Task<ActionResult> VolunteerClockingOne([FromBody] ReportModel model)
    {
        try
        {
            var (startUtc, endUtc) = LocalRangeToUtc(model);
            var record = await _db.Clockings.Where(e =>
                    e.VoluntId == model.Id && e.CreatedAt >= startUtc && e.CreatedAt < endUtc)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = OrgClock.ToLocal(e.CreatedAt)?.ToString("dd-MM-yyyy"),
                    ClockIn = OrgClock.ToLocal(e.ClockInTime)?.ToString("HH:mm:ss"),
                    ClockOut = OrgClock.ToLocal(e.ClockOutTime)?.ToString("HH:mm:ss"),
                    BreakStart = OrgClock.ToLocal(e.LeaveOnBreakTime)?.ToString("HH:mm:ss"),
                    BreakEnd = OrgClock.ToLocal(e.ReturnOnBreakTime)?.ToString("HH:mm:ss"),
                    Hours = e.WorkingHours != null ? $"{e.WorkingHours!.Value.Hours} hours {e.WorkingHours.Value.Minutes} minutes" : null
                }).ToList();

                var sumHours = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
                var sumMinutes = record.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
                var total = $"{sumHours + sumMinutes / 60} Hours {sumMinutes / 60} Minutes";

                return Ok(new ReportDataSet<Clockings>
                {
                    Success = true,
                    Detail = new Detail
                    {
                        Company = "Inprem Holistic Community Resource Center",
                        Address = "5757 Karl Road, Columbus, OH 43229",
                        Contact = "614-516-1812 | Inpremcommunitycenter@yahoo.com",
                        Duration = total
                    },
                    Contents = list
                });
            }

            return Ok(new ReportDataSet<Clockings>
            {
                Success = true,
                Detail = null,
                Contents = null
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false!);
    }
}
