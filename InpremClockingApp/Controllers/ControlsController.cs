
using InpremClockingApp.Data;
using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Controllers;

[Route("api/[controller]")]
public class ControlsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly StaffClockingService _staffClock;
    private readonly VolunteerClockingService _volunteerClock;

    public ControlsController(ApplicationDbContext db, StaffClockingService staffClock, VolunteerClockingService volunteerClock)
    {
        _db = db;
        _staffClock = staffClock;
        _volunteerClock = volunteerClock;
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
                var existing = await _staffClock.GetTodayRecord(id).ConfigureAwait(false);
                if (existing != null)
                {
                    return Conflict(existing.ClockOutTime != null
                        ? "You have already clocked in and out today. If you need to step away, use Leave for Break / Return from Break instead."
                        : "You have already clocked in today.");
                }

                var now = DateTime.UtcNow;
                var model = new ClockingStaff
                {
                    StafId = id,
                    FullName = staff.FullName,
                    ClockInTime = now,
                    ClockOutTime = null,
                    LeaveOnBreakTime = null,
                    ReturnOnBreakTime = null,
                    WorkingHours = null,
                    CreatedAt = now
                };

                var saved = await _staffClock.Create(model).ConfigureAwait(false);
                if (saved == null)
                {
                    // Lost a race with a concurrent clock-in for the same staff member/day.
                    return Conflict("You have already clocked in today.");
                }

                return Ok("You have successfully clocked in");
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
            if (staff != null)
            {
                var record = await _staffClock.GetTodayRecord(id).ConfigureAwait(false);

                if (record != null)
                {
                    if (record.ClockOutTime != null)
                    {
                        return Ok("You have already clocked out");
                    }

                    var success = await _staffClock.ClockOut(record).ConfigureAwait(false);
                    if (success)
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
            var record = await _staffClock.GetTodayRecord(id).ConfigureAwait(false);

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

                var success = await _staffClock.BreakStart(record).ConfigureAwait(false);
                if (success)
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
            var record = await _staffClock.GetTodayRecord(id).ConfigureAwait(false);

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

                var success = await _staffClock.BreakEnd(record).ConfigureAwait(false);
                if (success)
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
                var existing = await _volunteerClock.GetTodayRecord(id).ConfigureAwait(false);
                if (existing != null)
                {
                    return Conflict(existing.ClockOutTime != null
                        ? "You have already clocked in and out today. If you need to step away, use Leave for Break / Return from Break instead."
                        : "You have already clocked in today.");
                }

                var now = DateTime.UtcNow;
                var model = new Clocking()
                {
                    VoluntId = id,
                    FullName = staff.FullName,
                    ClockInTime = now,
                    ClockOutTime = null,
                    LeaveOnBreakTime = null,
                    ReturnOnBreakTime = null,
                    WorkingHours = null,
                    CreatedAt = now
                };

                var saved = await _volunteerClock.Create(model).ConfigureAwait(false);
                if (saved == null)
                {
                    // Lost a race with a concurrent clock-in for the same volunteer/day.
                    return Conflict("You have already clocked in today.");
                }

                return Ok("You have successfully clocked in");
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
            if (staff != null)
            {
                var record = await _volunteerClock.GetTodayRecord(id).ConfigureAwait(false);

                if (record != null)
                {
                    if (record.ClockOutTime != null)
                    {
                        return Ok("You have already clocked out");
                    }

                    var success = await _volunteerClock.ClockOut(record).ConfigureAwait(false);
                    if (success)
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
            var record = await _volunteerClock.GetTodayRecord(id).ConfigureAwait(false);

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

                var success = await _volunteerClock.BreakStart(record).ConfigureAwait(false);
                if (success)
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
            var record = await _volunteerClock.GetTodayRecord(id).ConfigureAwait(false);

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

                var success = await _volunteerClock.BreakEnd(record).ConfigureAwait(false);
                if (success)
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
