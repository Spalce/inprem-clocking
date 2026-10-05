using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class VolunteerClocking : PageModel
{
    private readonly VolunteerClockingService _service;
    private readonly VolunteerService _volunteer;
    private readonly ITenantClock _tenantClock;

    public VolunteerClocking(VolunteerClockingService service, VolunteerService volunteer, ITenantClock tenantClock)
    {
        _service = service;
        _volunteer = volunteer;
        _tenantClock = tenantClock;
    }

    public VolunteerClockingVm Model = new();

    // bind simple clocking inputs directly for the manual clocking form
    [BindProperty]
    public Clocking ClockingVolunteerProp { get; set; } = new();

    // Named "pageNumber" rather than "page" because "page" is a reserved Razor Pages route
    // value (the page's own path) - a query string "page" is intercepted by route-value model
    // binding before it ever reaches a same-named property.
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public async Task<IActionResult> OnGetAsync()
    {
        var paged = await _service.GetPaged(PageNumber, PageSize).ConfigureAwait(true);
        Model!.Clocking = paged.Items;
        Model.Volunteer = await _volunteer.GetAll().ConfigureAwait(true);

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
        Model.Volunteer = await _volunteer.GetAll().ConfigureAwait(true);

        ViewData["TotalCount"] = paged.TotalCount;
        ViewData["Page"] = paged.Page;
        ViewData["PageSize"] = paged.PageSize;
        ViewData["TotalPages"] = paged.TotalPages;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var volunteer = await _volunteer.GetById(ClockingVolunteerProp.VoluntId).ConfigureAwait(true);
        if (volunteer == null!)
            return RedirectToPage("./VolunteerClocking");

        var clocking = await _service.CheckToday(ClockingVolunteerProp).ConfigureAwait(true);
        if (clocking)
            return RedirectToPage("./VolunteerClocking");

        ClockingVolunteerProp.FullName = volunteer.FirstName + " " + volunteer.LastName;

        // ClockInTime/ClockOutTime, when submitted, come from a datetime-local input
        // and represent the org's local wall-clock time - convert to UTC for storage.
        ClockingVolunteerProp.ClockInTime = ClockingVolunteerProp.ClockInTime.HasValue
            ? _tenantClock.ToUtc(ClockingVolunteerProp.ClockInTime.Value)
            : DateTime.UtcNow;
        ClockingVolunteerProp.ClockOutTime = _tenantClock.ToUtc(ClockingVolunteerProp.ClockOutTime);

        // Catches exactly the bug that produced negative working hours: a manually-entered Clock
        // Out earlier than Clock In (e.g. meant for the next day, or an AM/PM slip). Nothing short
        // of this check stops that from being saved as-is.
        if (ClockingVolunteerProp.ClockOutTime.HasValue && ClockingVolunteerProp.ClockOutTime <= ClockingVolunteerProp.ClockInTime)
        {
            ViewData["Error"] = "Clock Out must be after Clock In.";
            return await RenderWithDataAsync().ConfigureAwait(true);
        }

        ClockingVolunteerProp.CreatedAt = DateTime.UtcNow;

        if (ClockingVolunteerProp.ClockOutTime != null)
            ClockingVolunteerProp.WorkingHours = ClockingVolunteerProp.ClockOutTime - ClockingVolunteerProp.ClockInTime;

        await _service.Create(ClockingVolunteerProp).ConfigureAwait(true);
        // repopulate list so the newly created clocking shows immediately
        return await RenderWithDataAsync().ConfigureAwait(true);
    }

    public async Task<IActionResult> OnPostClockOutAsync([FromBody] Clocking model)
    {
        var result = await _service.ClockOut(model).ConfigureAwait(true);
        if (result)
            return RedirectToPage("./VolunteerClocking");

        return RedirectToPage("./VolunteerClocking");
    }

    public async Task<IActionResult> OnPostBreakStartAsync([FromBody] Clocking model)
    {
        var result = await _service.BreakStart(model).ConfigureAwait(true);
        if (result)
            return RedirectToPage("./VolunteerClocking");

        return RedirectToPage("./VolunteerClocking");
    }

    public async Task<IActionResult> OnPostBreakEndAsync([FromBody] Clocking model)
    {
        var result = await _service.BreakEnd(model).ConfigureAwait(true);
        if (result)
            return RedirectToPage("./VolunteerClocking");

        return RedirectToPage("./VolunteerClocking");
    }

    // Corrects an existing, already-completed record (e.g. a bad manually-entered timestamp) -
    // the admin-side counterpart to the validation added to OnPostAsync above. Only the clocking
    // fields are editable; the owning volunteer and the record's day are not.
    public async Task<IActionResult> OnPostEditAsync([FromBody] Clocking model)
    {
        // ClockInTime/ClockOutTime/break times arrive as org-local wall-clock values from
        // datetime-local inputs - convert to UTC for storage, matching every other write path.
        var clockIn = _tenantClock.ToUtc(model.ClockInTime);
        var clockOut = _tenantClock.ToUtc(model.ClockOutTime);
        var leaveOnBreak = _tenantClock.ToUtc(model.LeaveOnBreakTime);
        var returnOnBreak = _tenantClock.ToUtc(model.ReturnOnBreakTime);

        var (success, error) = await _service.UpdateTimes(
            model.ClockingId, clockIn, clockOut, leaveOnBreak, returnOnBreak).ConfigureAwait(true);

        if (!success)
            return BadRequest(new { error });

        return new OkResult();
    }
}
