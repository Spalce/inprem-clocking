
using InpremClockingApp.Data;
using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Controllers;

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
            var staff = await _service.GetByEmail(model.EmailAddress!).ConfigureAwait(true);
            if (staff != null!)
                return BadRequest("Record already exists");

            model.CreatedAt = DateTime.Now;
            model.Type = "Staff";

            var save = await _service.Create(model).ConfigureAwait(true);
            if (save != null!)
            {
                return Ok(save);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return BadRequest(false);
    }

    [Produces("application/json")]
    [HttpPost("move")]
    public async Task<IActionResult> MoveToVolunteer([FromBody] Staff model)
    {
        try
        {
            Console.WriteLine("Initial");
            var volunteer = new Volunteer
            {
                EmailAddress = model.EmailAddress,
                FirstName = model.FirstName,
                LastName = model.LastName,
                ZipCode = model.ZipCode,
                Gender = model.Gender,
                Type = "Volunteer",
                PhoneNumber = model.PhoneNumber,
                Address = model.Address,
                CreatedAt = model.CreatedAt
            };

            var createVolunteer = await _volunteer.Create(volunteer).ConfigureAwait(true);
            if (createVolunteer != null!)
            {
                Console.WriteLine("Vol Created");
                var move = await _staffClock.GetAllById(model.StaffId).ConfigureAwait(false);
                if (move != null!)
                {
                    var clockingStaves = move.ToList();
                    foreach (var item in clockingStaves)
                    {
                        var clockings = new Clocking
                        {
                            VoluntId = createVolunteer.VolunteerId,
                            ClockInTime = item.ClockInTime,
                            ClockOutTime = item.ClockOutTime,
                            LeaveOnBreakTime = item.LeaveOnBreakTime,
                            ReturnOnBreakTime = item.ReturnOnBreakTime,
                            WorkingHours = item.WorkingHours,
                            CreatedAt = item.CreatedAt
                        };

                        await _volunteerClock.Create(clockings).ConfigureAwait(true);
                        await _staffClock.Delete(item).ConfigureAwait(false);
                        Console.WriteLine("Clock Moved");
                    }
                }
            }
            else
            {
                return NotFound("Volunteer could not be created");
            }

            return Ok("Move successful");
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
