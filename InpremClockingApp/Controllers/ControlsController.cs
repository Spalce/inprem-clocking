
using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Controllers;

[Route("api/[controller]")]
public class ControlsController : Controller
{
    private readonly ApplicationDbContext _db;

    public ControlsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [Produces("application/json")]
    [HttpGet("staff-clockin/{id:long}")]
    public async Task<IActionResult> StaffClockIn(long id)
    {
        try
        {
            var staff = await _db.Staffs.FindAsync(id).ConfigureAwait(false);
            if (staff != null)
            {
                var isExists = await _db.ClockingsStaff
                    .FirstOrDefaultAsync(e => e.StafId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                    .ConfigureAwait(false);

                if (isExists != null)
                {
                    return Ok("You have already clocked in");
                }

                var model = new ClockingStaff
                {
                    StafId = id,
                    FullName = staff.FullName,
                    ClockInTime = DateTime.Now,
                    ClockOutTime = null,
                    LeaveOnBreakTime = null,
                    ReturnOnBreakTime = null,
                    WorkingHours = null,
                    CreatedAt = DateTime.Now
                };

                await _db.ClockingsStaff.AddAsync(model).ConfigureAwait(false);
                var result = await _db.SaveChangesAsync();
                if (result > 0)
                {
                    return Ok("You have successfully clocked in");
                }
            }
            else
            {
                return BadRequest(false);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("staff-clockout/{id:long}")]
    public async Task<IActionResult> StaffClockOut(long id)
    {
        try
        {
            var staff = await _db.Staffs.FindAsync(id).ConfigureAwait(false);
            Console.WriteLine(staff);
            if (staff != null)
            {
                var record = await _db.ClockingsStaff
                    .FirstOrDefaultAsync(e => e.StafId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                    .ConfigureAwait(false);

                if (record != null)
                {
                    if (record.ClockOutTime != null)
                    {
                        return Ok("You have already clocked out");
                    }

                    if (record is { LeaveOnBreakTime: { }, ReturnOnBreakTime: null })
                    {
                        record.ReturnOnBreakTime = DateTime.Now;
                    }

                    record.ClockOutTime = DateTime.Now;
                    TimeSpan? main = DateTime.Now - record.ClockInTime;
                    TimeSpan? difference = null;
                    if (record is { LeaveOnBreakTime: { }, ReturnOnBreakTime: { } })
                    {
                        var leave = record.ReturnOnBreakTime - record.LeaveOnBreakTime;
                        difference = main - leave;
                    }
                    else
                    {
                        difference = main;
                    }
                    record.WorkingHours = difference;

                    _db.ClockingsStaff.Update(record);
                    var result = await _db.SaveChangesAsync();
                    if (result > 0)
                    {
                        return Ok("You have successfully clocked out");
                    }
                }
                else
                {
                    return Ok("No staff clocking found for today");
                }
            }
            else
            {
                return Ok("No staff matches this id provided");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("staff-leavein/{id:long}")]
    public async Task<IActionResult> StaffLeaveIn(long id)
    {
        try
        {
            var record = await _db.ClockingsStaff
                .FirstOrDefaultAsync(e => e.StafId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                .ConfigureAwait(false);

            if (record != null)
            {
                if (record.LeaveOnBreakTime != null)
                {
                    return Ok("You are already on break");
                }

                if (record.ClockOutTime != null)
                {
                    return Ok("You cannot take leave since you have already clocked out");
                }

                record.LeaveOnBreakTime = DateTime.Now;

                _db.ClockingsStaff.Update(record);
                var result = await _db.SaveChangesAsync();
                if (result > 0)
                {
                    return Ok("You have successfully clocked for a leave out");
                }
            }
            else
            {
                return Ok("No staff clocking exist for today");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("staff-leaveout/{id:long}")]
    public async Task<IActionResult> StaffLeaveOut(long id)
    {
        try
        {
            var record = await _db.ClockingsStaff
                .FirstOrDefaultAsync(e => e.StafId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                .ConfigureAwait(false);

            if (record != null)
            {
                if (record.LeaveOnBreakTime == null)
                {
                    return Ok("You cannot clock since you are not on break");
                }

                if (record.ReturnOnBreakTime != null)
                {
                    return Ok("You have already clocked to have returned from break");
                }

                record.ReturnOnBreakTime = DateTime.Now;

                _db.ClockingsStaff.Update(record);
                var result = await _db.SaveChangesAsync();
                if (result > 0)
                {
                    return Ok("You have successfully clocked to have returned fom break");
                }
            }
            else
            {
                return Ok("No staff clocking found for today");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("volunteer-clockin/{id:long}")]
    public async Task<IActionResult> VolunteerClockIn(long id)
    {
        try
        {
            var staff = await _db.Volunteers.FindAsync(id).ConfigureAwait(false);
            if (staff != null)
            {
                var isExists = await _db.Clockings
                    .FirstOrDefaultAsync(e => e.VoluntId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                    .ConfigureAwait(false);

                if (isExists != null)
                {
                    return Ok("You have already clocked in");
                }

                var model = new Clocking()
                {
                    VoluntId = id,
                    FullName = staff.FullName,
                    ClockInTime = DateTime.Now,
                    ClockOutTime = null,
                    LeaveOnBreakTime = null,
                    ReturnOnBreakTime = null,
                    WorkingHours = null,
                    CreatedAt = DateTime.Now
                };

                await _db.Clockings.AddAsync(model).ConfigureAwait(false);
                var result = await _db.SaveChangesAsync();
                if (result > 0)
                {
                    return Ok("You have successfully clocked in");
                }
            }
            else
            {
                return BadRequest(false);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("volunteer-clockout/{id:long}")]
    public async Task<IActionResult> VolunteerClockOut(long id)
    {
        try
        {
            var staff = await _db.Volunteers.FindAsync(id).ConfigureAwait(false);
            Console.WriteLine(staff);
            if (staff != null)
            {
                var record = await _db.Clockings
                    .FirstOrDefaultAsync(e => e.VoluntId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                    .ConfigureAwait(false);

                if (record != null)
                {
                    if (record.ClockOutTime != null)
                    {
                        return Ok("You have already clocked out");
                    }

                    if (record is { LeaveOnBreakTime: { }, ReturnOnBreakTime: null })
                    {
                        record.ReturnOnBreakTime = DateTime.Now;
                    }

                    record.ClockOutTime = DateTime.Now;
                    TimeSpan? main = DateTime.Now - record.ClockInTime;
                    TimeSpan? difference = null;
                    if (record is { LeaveOnBreakTime: { }, ReturnOnBreakTime: { } })
                    {
                        var leave = record.ReturnOnBreakTime - record.LeaveOnBreakTime;
                        difference = main - leave;
                    }
                    else
                    {
                        difference = main;
                    }
                    record.WorkingHours = difference;

                    _db.Clockings.Update(record);
                    var result = await _db.SaveChangesAsync();
                    if (result > 0)
                    {
                        return Ok("You have successfully clocked out");
                    }
                }
                else
                {
                    return Ok("No staff clocking found for today");
                }
            }
            else
            {
                return Ok("No staff matches this id provided");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("volunteer-leavein/{id:long}")]
    public async Task<IActionResult> VolunteerLeaveIn(long id)
    {
        try
        {
            var record = await _db.Clockings
                .FirstOrDefaultAsync(e => e.VoluntId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                .ConfigureAwait(false);

            if (record != null)
            {
                if (record.LeaveOnBreakTime != null)
                {
                    return Ok("You are already on break");
                }

                if (record.ClockOutTime != null)
                {
                    return Ok("You cannot take leave since you have already clocked out");
                }

                record.LeaveOnBreakTime = DateTime.Now;

                _db.Clockings.Update(record);
                var result = await _db.SaveChangesAsync();
                if (result > 0)
                {
                    return Ok("You have successfully clocked for a leave out");
                }
            }
            else
            {
                return Ok("No staff clocking exist for today");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("volunteer-leaveout/{id:long}")]
    public async Task<IActionResult> VolunteerLeaveOut(long id)
    {
        try
        {
            var record = await _db.Clockings
                .FirstOrDefaultAsync(e => e.VoluntId == id && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                .ConfigureAwait(false);

            if (record != null)
            {
                if (record.LeaveOnBreakTime == null)
                {
                    return Ok("You cannot clock since you are not on break");
                }

                if (record.ReturnOnBreakTime != null)
                {
                    return Ok("You have already clocked to have returned from break");
                }

                record.ReturnOnBreakTime = DateTime.Now;

                _db.Clockings.Update(record);
                var result = await _db.SaveChangesAsync();
                if (result > 0)
                {
                    return Ok("You have successfully clocked to have returned fom break");
                }
            }
            else
            {
                return Ok("No staff clocking found for today");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("get-logout")]
    public async Task<IActionResult> GetLogout(long id)
    {
        try
        {
            var record = await _db.Setting.FirstOrDefaultAsync().ConfigureAwait(false);

            if (record != null)
            {
                if (record.Action)
                {
                    return Ok(true);
                }
                else
                {
                    return Ok(record.Duration * 60);
                }
            }
            else
            {
                return BadRequest("No record found");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }
}
