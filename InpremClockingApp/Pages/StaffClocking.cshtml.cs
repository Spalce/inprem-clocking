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
    private readonly ITenantClock _tenantClock;

    public StaffClocking(StaffClockingService service, StaffService staff, ITenantClock tenantClock)
    {
        _service = service;
        _staff = staff;
        _tenantClock = tenantClock;
    }

    public StaffClockingVm Model = new();

    // bind simple clocking inputs directly for the manual clocking form
    [BindProperty]
    public ClockingStaff ClockingStaff { get; set; } = new();

    // pagination parameters. Named "pageNumber" rather than "page" because "page" is a
    // reserved Razor Pages route value (the page's own path) - a query string "page" is
    // intercepted by route-value model binding before it ever reaches a same-named property.
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public async Task<IActionResult> OnGetAsync()
    {
        var paged = await _service.GetPaged(PageNumber, PageSize).ConfigureAwait(true);
        Model!.Clocking = paged.Items;
        Model.Staff = await _staff.GetAll().ConfigureAwait(true);

        ViewData["TotalCount"] = paged.TotalCount;
        ViewData["Page"] = paged.Page;
        ViewData["PageSize"] = paged.PageSize;
        ViewData["TotalPages"] = paged.TotalPages;

        return Page();
    }

    private async Task<IActionResult> RenderWithDataAsync()
    {
        var paged = await _service.GetPaged(PageNumber, PageSize).ConfigureAwait(true);
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

        // ClockInTime/ClockOutTime, when submitted, come from a datetime-local input
        // and represent the org's local wall-clock time - convert to UTC for storage.
        ClockingStaff.ClockInTime = ClockingStaff.ClockInTime.HasValue
            ? _tenantClock.ToUtc(ClockingStaff.ClockInTime.Value)
            : DateTime.UtcNow;
        ClockingStaff.ClockOutTime = _tenantClock.ToUtc(ClockingStaff.ClockOutTime);

        // Catches exactly the bug that produced negative working hours: a manually-entered Clock
        // Out earlier than Clock In (e.g. meant for the next day, or an AM/PM slip). Nothing short
        // of this check stops that from being saved as-is.
        if (ClockingStaff.ClockOutTime.HasValue && ClockingStaff.ClockOutTime <= ClockingStaff.ClockInTime)
        {
            ViewData["Error"] = "Clock Out must be after Clock In.";
            return await RenderWithDataAsync().ConfigureAwait(true);
        }

        ClockingStaff.CreatedAt = DateTime.UtcNow;

        if (ClockingStaff.ClockOutTime != null)
            ClockingStaff.WorkingHours = ClockingStaff.ClockOutTime - ClockingStaff.ClockInTime;

        await _service.Create(ClockingStaff).ConfigureAwait(true);

        // repopulate list so the newly created clocking shows immediately
        return await RenderWithDataAsync().ConfigureAwait(true);
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

    // Corrects an existing, already-completed record (e.g. a bad manually-entered timestamp) -
    // the admin-side counterpart to the validation added to OnPostAsync above. Only the clocking
    // fields are editable; the owning staff member and the record's day are not.
    public async Task<IActionResult> OnPostEditAsync([FromBody] ClockingStaff model)
    {
        // ClockInTime/ClockOutTime/break times arrive as org-local wall-clock values from
        // datetime-local inputs - convert to UTC for storage, matching every other write path.
        var clockIn = _tenantClock.ToUtc(model.ClockInTime);
        var clockOut = _tenantClock.ToUtc(model.ClockOutTime);
        var leaveOnBreak = _tenantClock.ToUtc(model.LeaveOnBreakTime);
        var returnOnBreak = _tenantClock.ToUtc(model.ReturnOnBreakTime);

        var (success, error) = await _service.UpdateTimes(
            model.ClockingStaffId, clockIn, clockOut, leaveOnBreak, returnOnBreak).ConfigureAwait(true);

        if (!success)
            return BadRequest(new { error });

        return new OkResult();
    }
}
