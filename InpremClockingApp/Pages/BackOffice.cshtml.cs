using InpremClockingApp.Data;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Pages
{
    public class BackOfficeModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<AppUser> _user;

        public BackOfficeModel(ApplicationDbContext db, UserManager<AppUser> user)
        {
            _db = db;
            _user = user;
        }

        public Dashboard? Dashboard  = new();
        public Dashboard? ThisWeek  = new();
        public Dashboard? Today  = new();
        public Dashboard? ThisMonth  = new();

        public async Task OnGet()
        {
            var staffs = await _db.Staffs.ToListAsync().ConfigureAwait(true);
            var volunteers = await _db.Volunteers.ToListAsync().ConfigureAwait(true);
            var staffClocking = await _db.ClockingsStaff.ToListAsync().ConfigureAwait(true);
            var volunteerClocking = await _db.Clockings.ToListAsync().ConfigureAwait(true);
            var users = await _user.Users.ToListAsync().ConfigureAwait(true);

            if (staffs != null!)
            {
                Dashboard!.StaffCount = staffs.Count;

            }

            if (volunteers != null!)
            {
                Dashboard!.VolunteerCount = volunteers.Count;
            }

            if (users != null!)
            {
                Dashboard!.AdminCount = users.Count;
            }

            if (staffClocking != null!)
            {
                var sumHours = staffClocking.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
                var sumMinutes = staffClocking.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
                Dashboard!.StaffClocking = $"{sumHours + sumMinutes / 60} Hours {sumMinutes % 60} Minutes";
            }

            if (volunteerClocking != null!)
            {
                var sumHours = volunteerClocking.Sum(e => e.WorkingHours != null! ? e.WorkingHours.Value.Hours : 0);
                var sumMinutes = volunteerClocking.Sum(e => e.WorkingHours != null! ? e.WorkingHours!.Value.Minutes : 0);
                Dashboard!.VolunteerClocking = $"{sumHours + sumMinutes / 60} Hours {sumMinutes % 60} Minutes";
            }
        }
    }
}
