
using InpremClockingApp.Data;
using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InpremClockingApp.Helpers;

namespace InpremClockingApp.Controllers;

// Not currently called by any page in the UI (Manage Staff/Volunteers now uses Razor Page
// handlers directly), but this performs Staff/Volunteer create/move and the route is live and
// reachable regardless - gated the same as Manage Staff/Volunteers itself. See ROLES.md
// "Known gaps" - this controller previously had no [Authorize] at all.
[Authorize(Policy = "AdminOnly")]
[Route("api/[controller]")]
public class StaffController : Controller
{
    private readonly StaffService _service;
    private readonly ApplicationDbContext _db;
    private readonly VolunteerService _volunteer;
    private readonly StaffClockingService _staffClock;
    private readonly VolunteerClockingService _volunteerClock;

    public StaffController(StaffService service, ApplicationDbContext db,
        VolunteerService volunteer, StaffClockingService staffClock,
        VolunteerClockingService volunteerClock)
    {
        _service = service;
        _db = db;
        _volunteer = volunteer;
        _staffClock = staffClock;
        _volunteerClock = volunteerClock;
    }

    [Produces("application/json")]
    [HttpPost("save")]
    public async Task<IActionResult> StaffClockIn([FromBody] Staff model)
    {
        try
        {
            var staff = await _service.GetByEmail(model.EmailAddress!);

            if (staff == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Staff member not found. Please check the email address."
                });
            }

            var alreadyClockedIn = await _staffClock.CheckToday(new ClockingStaff
            {
                StafId = staff.StaffId
            });

            if (alreadyClockedIn)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Staff member has already clocked in today."
                });
            }

            var now = DateTime.UtcNow;
            var clocking = new ClockingStaff
            {
                StafId = staff.StaffId,
                CreatedAt = now,
                ClockInTime = now
            };

            var save = await _staffClock.Create(clocking);

            if (save == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Staff member has already clocked in today."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Clock-in successful.",
                data = save
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "Unable to process clock-in. Please try again."
            });
        }
    }

    //[Produces("application/json")]
    //[HttpPost("save")]
    //public async Task<IActionResult> StaffClockIn([FromBody] Staff model)
    //{
    //    try
    //    {
    //        var staff = await _service.GetByEmail(model.EmailAddress!).ConfigureAwait(true);
    //        if (staff == null)
    //        {
    //            return NotFound(new
    //            {
    //                success = false,
    //                message = "Staff member not found. Please check the email address."
    //            });
    //        }


    //        //model.CreatedAt = DateTime.Now;
    //        //model.Type = "Staff";

    //        var save = await _service.Create(model).ConfigureAwait(true);
    //        if (save != null!)
    //        {
    //            return Ok(save);
    //        }
    //    }
    //    catch (Exception e)
    //    {
    //        Console.WriteLine(e);
    //    }

    //    return BadRequest(false);
    //}

    [Produces("application/json")]
    [HttpPost("move")]
    public async Task<IActionResult> MoveToVolunteer([FromBody] Staff model)
    {
        try
        {
            // Copy from the stored Staff row (tenant-filtered), not the posted body, so the new
            // Volunteer always matches the staff member whose clockings are being moved.
            var staff = await _db.Staffs.FindAsync(model.StaffId).ConfigureAwait(false);
            if (staff == null)
                return NotFound("Staff member not found");

            var emailTaken = await _db.Volunteers
                .AnyAsync(v => v.EmailAddress == staff.EmailAddress)
                .ConfigureAwait(false);
            if (emailTaken)
                return Conflict("A volunteer with this email address already exists.");

            var volunteer = new Volunteer
            {
                TenantId = staff.TenantId,
                EmailAddress = staff.EmailAddress,
                FirstName = staff.FirstName,
                LastName = staff.LastName,
                ZipCode = staff.ZipCode,
                Gender = staff.Gender,
                Type = "Volunteer",
                PhoneNumber = staff.PhoneNumber,
                Address = staff.Address,
                CreatedAt = staff.CreatedAt
            };
            await _db.Volunteers.AddAsync(volunteer).ConfigureAwait(false);

            // Historical sessions are copied as-is, including their own ClockDate - deliberately
            // not via VolunteerClockingService.Create, which is for live clock-ins and stamps
            // ClockDate with today (so every copy after the first collided with the
            // one-session-per-day index and was dropped, while its original was still deleted).
            var staffClockings = await _db.ClockingsStaff
                .Where(c => c.StafId == staff.StaffId)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var item in staffClockings)
            {
                await _db.Clockings.AddAsync(new Clocking
                {
                    TenantId = item.TenantId,
                    Volunteer = volunteer,
                    FullName = item.FullName ?? staff.FullName,
                    ClockInTime = item.ClockInTime,
                    ClockOutTime = item.ClockOutTime,
                    LeaveOnBreakTime = item.LeaveOnBreakTime,
                    ReturnOnBreakTime = item.ReturnOnBreakTime,
                    WorkingHours = item.WorkingHours,
                    CreatedAt = item.CreatedAt,
                    ClockDate = item.ClockDate
                }).ConfigureAwait(false);
            }

            _db.ClockingsStaff.RemoveRange(staffClockings);

            // One SaveChanges = one transaction: the volunteer, every copied session and every
            // deletion commit together, or (on any failure) none of them do.
            await _db.SaveChangesAsync().ConfigureAwait(false);

            return Ok($"Move successful - {staffClockings.Count} clocking record(s) moved.");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpGet("all-staff")]
    public async Task<IEnumerable<Staff>> AllStaff()
    {
        try
        {
            var staff = await _service.GetAll().ConfigureAwait(true);

            return staff;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return null!;
    }

    [Produces("application/json")]
    [HttpGet("all-Volunteer")]
    public async Task<IEnumerable<Volunteer>> AllVolunteer()
    {
        try
        {
            var staff = await _volunteer.GetAll().ConfigureAwait(true);

            return staff;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return null!;
    }
}
