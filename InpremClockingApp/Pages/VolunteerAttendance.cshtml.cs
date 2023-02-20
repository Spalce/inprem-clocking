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

    public async void OnGet()
    {

    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input == null)
            return RedirectToPage("./VolunteerAttendance");

        var staff = await _db.Volunteers.FirstOrDefaultAsync(e =>
                e.EmailAddress == Input.EmailAddress && e.FirstName == Input.FirstName && e.LastName == Input.LastName)
            .ConfigureAwait(false);
        if (staff != null!)
            return RedirectToPage("./VolunteerAttendance");

        Input.CreatedAt = DateTime.Now;
        Input.Type = "Volunteer";

        await _db.Volunteers.AddAsync(Input).ConfigureAwait(false);
        await _db.SaveChangesAsync();
        TempData["Message"] = "Record saved successfully!";

        return RedirectToPage("./VolunteerAttendance");
    }
}
