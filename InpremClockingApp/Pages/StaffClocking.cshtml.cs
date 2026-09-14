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

    // bind simple clocking inputs directly for the manual clocking form
    [BindProperty]
    public ClockingStaff ClockingStaff { get; set; } = new();

    // pagination parameters
    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public async Task<IActionResult> OnGetAsync()
    {
        var paged = await _service.GetPaged(Page, PageSize).ConfigureAwait(true);
        Model!.Clocking = paged.Items;
        Model.Staff = await _staff.GetAll().ConfigureAwait(true);

        ViewData["TotalCount"] = paged.TotalCount;
        ViewData["Page"] = paged.Page;
        ViewData["PageSize"] = paged.PageSize;
        ViewData["TotalPages"] = paged.TotalPages;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var staff = await _staff.GetById(ClockingStaff.StafId).ConfigureAwait(true);
        if (staff == null!)
            return RedirectToPage("./StaffClocking");

        var clocking = await _service.CheckToday(ClockingStaff).ConfigureAwait(true);
        if (clocking)
            return RedirectToPage("./StaffClocking");

        ClockingStaff.FullName = staff.FirstName + " " + staff.LastName;
        ClockingStaff.CreatedAt = DateTime.Now;
        if (ClockingStaff.ClockInTime == null)
            ClockingStaff.ClockInTime = DateTime.Now;
        if (ClockingStaff.ClockOutTime != null)
            ClockingStaff.WorkingHours = ClockingStaff.ClockOutTime - ClockingStaff.ClockInTime;

        await _service.Create(ClockingStaff).ConfigureAwait(true);

        // repopulate list so the newly created clocking shows immediately
        var paged = await _service.GetPaged(Page, PageSize).ConfigureAwait(true);
        Model!.Clocking = paged.Items;
        Model.Staff = await _staff.GetAll().ConfigureAwait(true);

        ViewData["TotalCount"] = paged.TotalCount;
        ViewData["Page"] = paged.Page;
        ViewData["PageSize"] = paged.PageSize;
        ViewData["TotalPages"] = paged.TotalPages;

        return Page();
    }

    public async Task<IActionResult> OnPostClockOutAsync([FromBody] ClockingStaff model)
    {
        var result = await _service.ClockOut(model).ConfigureAwait(true);
        if (result)
            return RedirectToPage("./StaffClocking");

        return RedirectToPage("./StaffClocking");
    }

    public async Task<IActionResult> OnPostBreakStartAsync([FromBody] ClockingStaff model)
    {
        var result = await _service.BreakStart(model).ConfigureAwait(true);
        if (result)
            return RedirectToPage("./StaffClocking");

        return RedirectToPage("./StaffClocking");
    }

    public async Task<IActionResult> OnPostBreakEndAsync([FromBody] ClockingStaff model)
    {
        var result = await _service.BreakEnd(model).ConfigureAwait(true);
        if (result)
            return RedirectToPage("./StaffClocking");

        return RedirectToPage("./StaffClocking");
    }
}
