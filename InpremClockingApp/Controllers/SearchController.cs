using InpremClockingApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Controllers;

// Matches its only callers (StaffAttendance/VolunteerAttendance), which are gated with a plain
// [Authorize] too, not AdminOnly. See ROLES.md "Known gaps" - this controller previously had no
// [Authorize] at all.
[Authorize]
[Route("api/[controller]")]
public class SearchController : Controller
{
    private readonly ApplicationDbContext _db;

    public SearchController(ApplicationDbContext db)
    {
        _db = db;
    }

    [Produces("application/json")]
    [HttpGet("staff")]
    public async Task<IActionResult> Staff()
    {
        try
        {
            string term = HttpContext.Request.Query["term"].ToString();
            var item = await _db.Staffs
                .Where(v => v.EmailAddress!.Contains(term) ||
                               v.FirstName!.Contains(term) ||
                               v.LastName!.Contains(term))
                .Select(v => new { email = v.EmailAddress, name = v.FirstName + " " + v.LastName })
                .ToListAsync();
            return Ok(item);
        }
        catch (Exception)
        {
            return BadRequest();
        }
    }

    [Produces("application/json")]
    [HttpGet("volunteer")]
    public async Task<IActionResult> Volunteer()
    {
        try
        {
            string term = HttpContext.Request.Query["term"].ToString();
            var item = await _db.Volunteers
                .Where(v => v.EmailAddress!.Contains(term) ||
                               v.FirstName!.Contains(term) ||
                               v.LastName!.Contains(term))
                .Select(v => new { email = v.EmailAddress, name = v.FirstName + " " + v.LastName })
                .ToListAsync();
            return Ok(item);
        }
        catch (Exception)
        {
            return BadRequest();
        }
    }

    [Produces("application/json")]
    [HttpGet("staff-data/{email}")]
    public async Task<IActionResult> StaffData(string email)
    {
        try
        {
            var item = await _db.Staffs.FirstOrDefaultAsync(v => v.EmailAddress == email);
            return Ok(item);
        }
        catch (Exception)
        {
            return BadRequest();
        }
    }

    [Produces("application/json")]
    [HttpGet("volunteer-data/{email}")]
    public async Task<IActionResult> VolunteerData(string email)
    {
        try
        {
            var item = await _db.Volunteers.FirstOrDefaultAsync(v => v.EmailAddress == email);
            return Ok(item);
        }
        catch (Exception)
        {
            return BadRequest();
        }
    }
}
