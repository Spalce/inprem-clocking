using InpremClockingApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PeopleController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PeopleController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET api/people/emails?q=prefix
        [HttpGet("emails")]
        public async Task<IActionResult> GetEmails([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Ok(new string[0]);

            var prefix = q.Trim();

            // Search both Staffs and Volunteers for matching emails, case-insensitive, limit results
            var staffEmails = _db.Staffs
                .Where(s => !string.IsNullOrEmpty(s.EmailAddress) && EF.Functions.Like(s.EmailAddress, prefix + "%"))
                .Select(s => s.EmailAddress);

            var volunteerEmails = _db.Volunteers
                .Where(v => !string.IsNullOrEmpty(v.EmailAddress) && EF.Functions.Like(v.EmailAddress, prefix + "%"))
                .Select(v => v.EmailAddress);

            var combined = await staffEmails.Union(volunteerEmails)
                .Where(e => e != null)
                .Distinct()
                .OrderBy(e => e)
                .Take(25)
                .ToListAsync();

            return Ok(combined);
        }
    }
}
