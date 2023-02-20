using InpremClockingApp.Data;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace InpremClockingApp.Pages;

public class Staff : PageModel
{
    private readonly StaffService _service;
    private readonly ApplicationDbContext _db;

    public Staff(StaffService service, ApplicationDbContext db)
    {
        _service = service;
        _db = db;
    }

    public IEnumerable<Models.Staff>? Staffs { get; set; }
    public Models.Staff? Model { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var model = await _service.GetAll().ConfigureAwait(true);
        if (model != null!)
            Staffs = model;
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] Models.Staff model)
    {
        if (!ModelState.IsValid)
            return RedirectToPage("./Staff");

        var staff = await _service.GetByEmail(model.EmailAddress!).ConfigureAwait(true);
        if (staff != null!)
            return RedirectToPage("./Staff");

        model.CreatedAt = DateTime.Now;
        model.Type = "Staff";

        var save = await _service.Create(model).ConfigureAwait(true);
        if (save != null!)
        {
            return RedirectToPage("./Staff");
        }

        return RedirectToPage("./Staff");
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] Models.Staff model)
    {
        if (!ModelState.IsValid)
            return RedirectToPage("./Staff");

        var save = await _service.Update(model).ConfigureAwait(true);
        if (save != null!)
        {
            return RedirectToPage("./Staff");
        }

        return RedirectToPage("./Staff");
    }

    public async Task<IActionResult> OnPostMoveAsync([FromBody] Models.Staff model)
    {
        if (!ModelState.IsValid)
            return RedirectToPage("./Staff");

        var volunteer = new Models.Volunteer
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

        var createVolunteer = await _db.Volunteers.AddAsync(volunteer).ConfigureAwait(false);
        var save = await _db.SaveChangesAsync().ConfigureAwait(false);
        if (save > 0)
        {
            var move = await _db.ClockingsStaff.Where(e => e.StafId == model.StaffId).ToListAsync()
                .ConfigureAwait(false);
            if (move != null!)
            {
                foreach (var item in move)
                {
                    var clockings = new Models.Clocking
                    {
                        VoluntId = createVolunteer.Entity.VolunteerId,
                        ClockInTime = item.ClockInTime,
                        ClockOutTime = item.ClockOutTime,
                        LeaveOnBreakTime = item.LeaveOnBreakTime,
                        ReturnOnBreakTime = item.ReturnOnBreakTime,
                        WorkingHours = item.WorkingHours,
                        CreatedAt = item.CreatedAt
                    };

                    await _db.Clockings.AddAsync(clockings).ConfigureAwait(true);
                    var saved = await _db.SaveChangesAsync().ConfigureAwait(true);
                    if (saved > 0)
                    {
                        _db.ClockingsStaff.Remove(item);
                        await _db.SaveChangesAsync();
                    }
                }
            }
        }
        else { return RedirectToPage("./Staff"); }

        _db.Staffs.Remove(model);
        await _db.SaveChangesAsync().ConfigureAwait(true);

        return RedirectToPage("./Staff");
    }
}
