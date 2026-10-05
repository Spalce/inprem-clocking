using InpremClockingApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Controllers
{
    // All callers (Manage Staff/Volunteers, Staff/Volunteer List reports) are AdminOnly pages.
    // See ROLES.md "Known gaps" - this controller previously had no [Authorize] at all.
    [Authorize(Policy = "AdminOnly")]
    [Route("api/[controller]")]
    [ApiController]
    public class PeopleController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PeopleController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET api/people/emails?q=prefix&type=staff|volunteer
        // "type" scopes the search to one domain - Manage Staff/Staff Report must only suggest
        // staff emails, Manage Volunteers/Volunteer Report only volunteer emails. Omitting it
        // searches both (kept only as a fallback; every current caller always passes it).
        [HttpGet("emails")]
        public async Task<IActionResult> GetEmails([FromQuery] string q, [FromQuery] string? type = null)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Ok(Array.Empty<object>());

            var prefix = q.Trim();

            var staffMatchesTask = type == "volunteer"
                ? Task.FromResult(new List<EmailMatch>())
                : _db.Staffs
                    .Where(s => !string.IsNullOrEmpty(s.EmailAddress) && EF.Functions.Like(s.EmailAddress, prefix + "%"))
                    .Select(s => new EmailMatch(s.EmailAddress!, s.FirstName + " " + s.LastName))
                    .ToListAsync();

            var volunteerMatchesTask = type == "staff"
                ? Task.FromResult(new List<EmailMatch>())
                : _db.Volunteers
                    .Where(v => !string.IsNullOrEmpty(v.EmailAddress) && EF.Functions.Like(v.EmailAddress, prefix + "%"))
                    .Select(v => new EmailMatch(v.EmailAddress!, v.FirstName + " " + v.LastName))
                    .ToListAsync();

            var staffMatches = await staffMatchesTask;
            var volunteerMatches = await volunteerMatchesTask;

            var combined = staffMatches.Concat(volunteerMatches)
                .GroupBy(e => e.Email)
                .Select(g => new { email = g.Key, name = g.First().Name })
                .OrderBy(e => e.email)
                .Take(25)
                .ToList();

            return Ok(combined);
        }

        private record EmailMatch(string Email, string Name);
    }
}
