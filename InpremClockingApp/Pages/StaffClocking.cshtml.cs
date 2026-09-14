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

    // pagination parameters
    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    [BindProperty(SupportsGet = true)]
    public int? StaffId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? End { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var paged = await _service.GetPaged(Page, PageSize, StaffId, Start, End).ConfigureAwait(true);
        Model!.Clocking = paged.Items;
        Model.Staff = await _staff.GetAll().ConfigureAwait(true);

        // expose paging info via ViewData
        ViewData["TotalCount"] = paged.TotalCount;
        ViewData["Page"] = paged.Page;
        ViewData["PageSize"] = paged.PageSize;
        ViewData["TotalPages"] = paged.TotalPages;
        ViewData["StaffId"] = StaffId;
        ViewData["Start"] = Start?.ToString("yyyy-MM-ddTHH:mm");
        ViewData["End"] = End?.ToString("yyyy-MM-ddTHH:mm");

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
