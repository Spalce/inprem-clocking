using InpremClockingApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Pages;

public class VolunteerAttendance : PageModel
{
    private readonly ApplicationDbContext _db;

    public VolunteerAttendance(ApplicationDbContext db)
    {
        _db = db;
    }

    public int TimeOut { get; set; }
    [BindProperty] public Models.Volunteer? Input { get; set; }

    public Task OnGetAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (Input == null)
            return RedirectToPage("./VolunteerAttendance");

        // Check if a volunteer with the same email already exists
        var existing = await _db.Volunteers
            .FirstOrDefaultAsync(e => e.EmailAddress == Input.EmailAddress)
            .ConfigureAwait(false);

        if (existing != null)
        {
            TempData["Message"] = "A volunteer with this email address is already registered.";
            return RedirectToPage("./VolunteerAttendance");
        }

        Input.CreatedAt = DateTime.Now;
        Input.Type = "Volunteer";

        await _db.Volunteers.AddAsync(Input).ConfigureAwait(false);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        TempData["Message"] = "Volunteer registered successfully!";
        return RedirectToPage("./VolunteerAttendance");
    }
}
