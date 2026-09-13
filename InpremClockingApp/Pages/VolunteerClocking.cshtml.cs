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

    public async Task<IActionResult> OnPostAsync([FromForm] VolunteerClockingVm model)
    {
        if (!ModelState.IsValid)
            return RedirectToPage("./VolunteerClocking");

        var staff = await _volunteer.GetById(model.ClockingVolunteer!.VoluntId).ConfigureAwait(true);
        if (staff == null!)
            return RedirectToPage("./VolunteerClocking");

        var clocking = await _service.CheckToday(model.ClockingVolunteer).ConfigureAwait(true);
        if (clocking)
            return RedirectToPage("./StaffClocking");

        model.ClockingVolunteer!.FullName = staff.FirstName + " " + staff.LastName;
        model.ClockingVolunteer!.CreatedAt = DateTime.Now;
        if (model.ClockingVolunteer.ClockOutTime != null)
            model.ClockingVolunteer.WorkingHours = model.ClockingVolunteer.ClockOutTime - model.ClockingVolunteer.ClockInTime;

        var save = await _service.Create(model.ClockingVolunteer!).ConfigureAwait(true);
        if (save != null!)
        {
            return RedirectToPage("./VolunteerClocking");
        }

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
