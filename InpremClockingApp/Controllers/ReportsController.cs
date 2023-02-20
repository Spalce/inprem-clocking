
using InpremClockingApp.Data;
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

    [Produces("application/json")]
    [HttpPost("staff")]
    public async Task<ActionResult<ReportDataSet<Content>>> GetStaff([FromBody] ReportModel model)
    {
        try
        {
            var record = await _db.Staffs.Where(e =>
                    e.CreatedAt!.Value.Date >= model.StartDate && e.CreatedAt.Value.Date <= model.EndDate)
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
                    Date = e.CreatedAt!.Value.Date.ToString("dd-MM-yyyy"),
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
            var record = await _db.Volunteers.Where(e =>
                    e.CreatedAt!.Value.Date >= model.StartDate && e.CreatedAt.Value.Date <= model.EndDate)
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
                    Date = e.CreatedAt!.Value.Date.ToString("dd-MM-yyyy")
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
            var record = await _db.ClockingsStaff
                .Where(e => e.CreatedAt!.Value.Date >= model.StartDate && e.CreatedAt.Value.Date <= model.EndDate)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = e.CreatedAt!.Value.Date.ToString("dd-MM-yyyy"),
                    ClockIn = e.ClockInTime != null ? e.ClockInTime!.Value.ToString("HH:mm:ss") : null,
                    ClockOut = e.ClockOutTime != null ? e.ClockOutTime!.Value.ToString("HH:mm:ss") : null,
                    BreakStart = e.LeaveOnBreakTime != null ? e.LeaveOnBreakTime!.Value.ToString("HH:mm:ss") : null,
                    BreakEnd = e.ReturnOnBreakTime != null ? e.ReturnOnBreakTime!.Value.ToString("HH:mm:ss") : null,
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
            var record = await _db.ClockingsStaff
                .Where(e => e.StafId == model.Id && e.CreatedAt!.Value.Date >= model.StartDate && e.CreatedAt.Value.Date <= model.EndDate)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = e.CreatedAt!.Value.Date.ToString("dd-MM-yyyy"),
                    ClockIn = e.ClockInTime != null ? e.ClockInTime!.Value.ToString("HH:mm:ss") : null,
                    ClockOut = e.ClockOutTime != null ? e.ClockOutTime!.Value.ToString("HH:mm:ss") : null,
                    BreakStart = e.LeaveOnBreakTime != null ? e.LeaveOnBreakTime!.Value.ToString("HH:mm:ss") : null,
                    BreakEnd = e.ReturnOnBreakTime != null ? e.ReturnOnBreakTime!.Value.ToString("HH:mm:ss") : null,
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
            var record = await _db.Clockings.Where(e =>
                    e.CreatedAt!.Value.Date >= model.StartDate && e.CreatedAt.Value.Date <= model.EndDate)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = e.CreatedAt!.Value.Date.ToString("dd-MM-yyyy"),
                    ClockIn = e.ClockInTime != null ? e.ClockInTime!.Value.ToString("HH:mm:ss") : null,
                    ClockOut = e.ClockOutTime != null ? e.ClockOutTime!.Value.ToString("HH:mm:ss") : null,
                    BreakStart = e.LeaveOnBreakTime != null ? e.LeaveOnBreakTime!.Value.ToString("HH:mm:ss") : null,
                    BreakEnd = e.ReturnOnBreakTime != null ? e.ReturnOnBreakTime!.Value.ToString("HH:mm:ss") : null,
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

    [Produces("application/json")]
    [HttpPost("volunteer-clocking-one")]
    public async Task<ActionResult> VolunteerClockingOne([FromBody] ReportModel model)
    {
        try
        {
            var record = await _db.Clockings.Where(e =>
                    e.VoluntId == model.Id && e.CreatedAt!.Value.Date >= model.StartDate && e.CreatedAt.Value.Date <= model.EndDate)
                .ToListAsync();
            if (record != null!)
            {
                // var list = new List<Content>();
                var list = record.Select(e => new Clockings
                {
                    Name = e.FullName,
                    Date = e.CreatedAt!.Value.Date.ToString("dd-MM-yyyy"),
                    ClockIn = e.ClockInTime != null ? e.ClockInTime!.Value.ToString("HH:mm:ss") : null,
                    ClockOut = e.ClockOutTime != null ? e.ClockOutTime!.Value.ToString("HH:mm:ss") : null,
                    BreakStart = e.LeaveOnBreakTime != null ? e.LeaveOnBreakTime!.Value.ToString("HH:mm:ss") : null,
                    BreakEnd = e.ReturnOnBreakTime != null ? e.ReturnOnBreakTime!.Value.ToString("HH:mm:ss") : null,
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
