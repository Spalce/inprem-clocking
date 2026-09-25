using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class VolunteerClocking : PageModel
{
    private readonly VolunteerClockingService _service;
    private readonly VolunteerService _volunteer;

    public VolunteerClocking(VolunteerClockingService service, VolunteerService volunteer)
    {
        _service = service;
        _volunteer = volunteer;
    }

    public VolunteerClockingVm Model = new();

    // bind simple clocking inputs directly for the manual clocking form
    [BindProperty]
    public Clocking ClockingVolunteerProp { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public async Task<IActionResult> OnGetAsync()
    {
        var paged = await _service.GetPaged(Page, PageSize).ConfigureAwait(true);
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
            ? OrgClock.ToUtc(ClockingVolunteerProp.ClockInTime.Value)
            : DateTime.UtcNow;
        ClockingVolunteerProp.ClockOutTime = OrgClock.ToUtc(ClockingVolunteerProp.ClockOutTime);
        ClockingVolunteerProp.CreatedAt = DateTime.UtcNow;

        if (ClockingVolunteerProp.ClockOutTime != null)
            ClockingVolunteerProp.WorkingHours = ClockingVolunteerProp.ClockOutTime - ClockingVolunteerProp.ClockInTime;

        var save = await _service.Create(ClockingVolunteerProp).ConfigureAwait(true);
        // repopulate list so the newly created clocking shows immediately
        var paged = await _service.GetPaged(Page, PageSize).ConfigureAwait(true);
        Model!.Clocking = paged.Items;
        Model.Volunteer = await _volunteer.GetAll().ConfigureAwait(true);

        ViewData["TotalCount"] = paged.TotalCount;
        ViewData["Page"] = paged.Page;
        ViewData["PageSize"] = paged.PageSize;
        ViewData["TotalPages"] = paged.TotalPages;

        return Page();
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
}
