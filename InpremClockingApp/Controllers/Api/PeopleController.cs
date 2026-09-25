using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class PeopleController : ControllerBase
{
    private readonly StaffService _staff;
    private readonly VolunteerService _volunteer;

    public PeopleController(StaffService staff, VolunteerService volunteer)
    {
        _staff = staff;
        _volunteer = volunteer;
    }

    [HttpGet("staff")]
    public async Task<IActionResult> SearchStaff([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _staff.SearchByName(q, page, pageSize);
        var simplified = result.Items.Select(s => new { id = s.StaffId, text = s.FirstName + " " + s.LastName, email = s.EmailAddress });
        return Ok(new { items = simplified, total = result.TotalCount });
    }

    [HttpGet("volunteers")]
    public async Task<IActionResult> SearchVolunteers([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _volunteer.SearchByName(q, page, pageSize);
        var simplified = result.Items.Select(v => new { id = v.VolunteerId, text = v.FirstName + " " + v.LastName, email = v.EmailAddress });
        return Ok(new { items = simplified, total = result.TotalCount });
    }
}
