using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;
[IgnoreAntiforgeryToken]
public class StaffClocking : PageModel
{
    private readonly StaffClockingService _service;
    private readonly StaffService _staff;

    public StaffClocking(StaffClockingService service, StaffService staff)
    {
        _service = service;
        _staff = staff;
    }

    public StaffClockingVm Model = new();

    public async Task<IActionResult> OnGetAsync()
    {
        Model!.Clocking = await _service.GetAll().ConfigureAwait(true);
        Model.Staff = await _staff.GetAll().ConfigureAwait(true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync([FromForm] StaffClockingVm model)
    {
        if (!ModelState.IsValid)
            return RedirectToPage("./StaffClocking");

        var staff = await _staff.GetById(model.ClockingStaff!.StafId).ConfigureAwait(true);
        if (staff == null!)
            return RedirectToPage("./StaffClocking");

        var clocking = await _service.CheckToday(model.ClockingStaff).ConfigureAwait(true);
        if (clocking)
            return RedirectToPage("./StaffClocking");

        model.ClockingStaff!.FullName = staff.FirstName + " " + staff.LastName;
        model.ClockingStaff!.CreatedAt = DateTime.Now;
        if (model.ClockingStaff.ClockOutTime != null)
            model.ClockingStaff.WorkingHours = model.ClockingStaff.ClockOutTime - model.ClockingStaff.ClockInTime;

        var save = await _service.Create(model.ClockingStaff!).ConfigureAwait(true);
        if (save != null!)
        {
            return RedirectToPage("./StaffClocking");
        }
        // Console.WriteLine($"{model.ClockingStaff!.StafId} {model.ClockingStaff.ClockInTime} {model.ClockingStaff.ClockOutTime}");
        return RedirectToPage("./StaffClocking");
    }

    public async Task<IActionResult> OnPostClockOutAsync([FromBody] ClockingStaff model)
    {
        var result = await _service.ClockOut(model).ConfigureAwait(true);
        if (result)
        {
            return RedirectToPage("./StaffClocking");
        }

        return RedirectToPage("./StaffClocking");
    }

    public async Task<IActionResult> OnPostBreakStartAsync([FromBody] ClockingStaff model)
    {
        var result = await _service.BreakStart(model).ConfigureAwait(true);
        if (result)
        {
            return RedirectToPage("./StaffClocking");
        }

        return RedirectToPage("./StaffClocking");
    }

    public async Task<IActionResult> OnPostBreakEndAsync([FromBody] ClockingStaff model)
    {
        var result = await _service.BreakEnd(model).ConfigureAwait(true);
        if (result)
        {
            return RedirectToPage("./StaffClocking");
        }

        return RedirectToPage("./StaffClocking");
    }
}
