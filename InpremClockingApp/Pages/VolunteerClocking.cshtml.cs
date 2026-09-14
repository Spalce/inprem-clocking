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

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    [BindProperty]
    public Clocking? ClockingVolunteer { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // restore initial behavior: paging only
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
        if (ClockingVolunteer == null || ClockingVolunteer.VoluntId == 0)
            return RedirectToPage("./VolunteerClocking");

        var volunteer = await _volunteer.GetById(ClockingVolunteer.VoluntId).ConfigureAwait(true);
        if (volunteer == null)
            return RedirectToPage("./VolunteerClocking");

        var alreadyClockedIn = await _service.CheckToday(ClockingVolunteer).ConfigureAwait(true);
        if (alreadyClockedIn)
            return RedirectToPage("./VolunteerClocking");

        ClockingVolunteer.FullName = volunteer.FirstName + " " + volunteer.LastName;
        ClockingVolunteer.CreatedAt = DateTime.Now;
        if (ClockingVolunteer.ClockInTime == null)
            ClockingVolunteer.ClockInTime = DateTime.Now;
        if (ClockingVolunteer.ClockOutTime != null)
            ClockingVolunteer.WorkingHours = ClockingVolunteer.ClockOutTime - ClockingVolunteer.ClockInTime;

        await _service.Create(ClockingVolunteer).ConfigureAwait(true);

        return RedirectToPage("./VolunteerClocking");
    }

    public async Task<IActionResult> OnPostClockOutAsync([FromBody] Clocking model)
    {
        var result = await _service.ClockOut(model).ConfigureAwait(true);
        if (result)
        {
            return RedirectToPage("./VolunteerClocking");
        }

        return RedirectToPage("./VolunteerClocking");
    }

    public async Task<IActionResult> OnPostBreakStartAsync([FromBody] Clocking model)
    {
        var result = await _service.BreakStart(model).ConfigureAwait(true);
        if (result)
        {
            return RedirectToPage("./VolunteerClocking");
        }

        return RedirectToPage("./VolunteerClocking");
    }

    public async Task<IActionResult> OnPostBreakEndAsync([FromBody] Clocking model)
    {
        var result = await _service.BreakEnd(model).ConfigureAwait(true);
        if (result)
        {
            return RedirectToPage("./VolunteerClocking");
        }

        return RedirectToPage("./VolunteerClocking");
    }
}
