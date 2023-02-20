using InpremClockingApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace InpremClockingApp.Pages;

public class StaffAttendance : PageModel
{
    private readonly ApplicationDbContext _db;

    public StaffAttendance(ApplicationDbContext db)
    {
        _db = db;
    }

    public int TimeOut { get; set; }
    [BindProperty] public Models.Staff? Input { get; set; }

    public async void OnGet()
    {

    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input == null)
            return RedirectToPage("./StaffAttendance");

        var staff = await _db.Staffs.FirstOrDefaultAsync(e =>
                e.EmailAddress == Input.EmailAddress && e.FirstName == Input.FirstName && e.LastName == Input.LastName)
            .ConfigureAwait(false);
        if (staff != null!)
            return RedirectToPage("./StaffAttendance");

        Input.CreatedAt = DateTime.Now;
        Input.Type = "Staff";

        await _db.Staffs.AddAsync(Input).ConfigureAwait(false);
        await _db.SaveChangesAsync();
        TempData["Message"] = "Record saved successfully!";

        return RedirectToPage("./StaffAttendance");
    }
}
