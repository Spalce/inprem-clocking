using InpremClockingApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

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

    public Task OnGetAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (Input == null)
            return RedirectToPage("./StaffAttendance");

        // Check if a staff member with the same email already exists
        var existing = await _db.Staffs
            .FirstOrDefaultAsync(e => e.EmailAddress == Input.EmailAddress)
            .ConfigureAwait(false);

        if (existing != null)
        {
            TempData["Message"] = "A staff member with this email address is already registered.";
            return RedirectToPage("./StaffAttendance");
        }

        Input.CreatedAt = DateTime.Now;
        Input.Type = "Staff";

        await _db.Staffs.AddAsync(Input).ConfigureAwait(false);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        TempData["Message"] = "Staff registered successfully!";
        return Redirect($"/staff-clockin/{Input.StaffId}");
    }
}
