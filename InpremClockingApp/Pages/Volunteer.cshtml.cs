using InpremClockingApp.Data;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Pages;

public class Volunteer : PageModel
{
    private readonly VolunteerService _service;
    private readonly ApplicationDbContext _db;

    public Volunteer(VolunteerService service, ApplicationDbContext db)
    {
        _service = service;
        _db = db;
    }

    public IEnumerable<Models.Volunteer>? Volunteers { get; set; }
    public Models.Volunteer? Model { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    // Named "pageNumber" rather than "page" because "page" is a reserved Razor Pages route
    // value (the page's own path) - a query string "page" is intercepted by route-value model
    // binding before it ever reaches a same-named property.
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await _service.SearchByName(Search, PageNumber, PageSize).ConfigureAwait(true);
        Volunteers = result.Items;

        ViewData["Search"] = Search;
        ViewData["TotalCount"] = result.TotalCount;
        ViewData["Page"] = result.Page;
        ViewData["PageSize"] = result.PageSize;
        ViewData["TotalPages"] = result.TotalPages;

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] Models.Volunteer model)
    {
        // Server-side validation
        if (string.IsNullOrWhiteSpace(model.EmailAddress))
        {
            ModelState.AddModelError("EmailAddress", "Email address is required");
        }

        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(model.EmailAddress))
        {
            ModelState.AddModelError("EmailAddress", "Please enter a valid email address");
        }

        // FirstName / LastName are optional for new registrations (search-by-email workflows).
        // Normalize missing names to empty strings so DB inserts don't fail with NULL.
        if (string.IsNullOrWhiteSpace(model.FirstName)) model.FirstName = string.Empty;
        if (string.IsNullOrWhiteSpace(model.LastName)) model.LastName = string.Empty;

        var volunteer = await _service.GetByEmail(model.EmailAddress).ConfigureAwait(true);
        if (volunteer != null)
        {
            ModelState.AddModelError("EmailAddress", "A volunteer with this email already exists");
        }

        if (!ModelState.IsValid)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var errors = ModelState.Where(kvp => kvp.Value.Errors.Count > 0)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray());
                return BadRequest(new { errors });
            }
            return RedirectToPage("./Volunteer");
        }

        model.CreatedAt = DateTime.UtcNow;
        model.Type = "Volunteer";

        try
        {
            var save = await _service.Create(model).ConfigureAwait(true);
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new ObjectResult(save) { StatusCode = StatusCodes.Status201Created };
            }
            return RedirectToPage("./Volunteer");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbex)
        {
            var msg = dbex.InnerException?.Message ?? dbex.Message;
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return BadRequest(new { error = "Database update failed: " + msg });
            ModelState.AddModelError(string.Empty, "Database update failed: " + msg);
            return RedirectToPage("./Volunteer");
        }
        catch (ArgumentException ae)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return BadRequest(new { error = ae.Message });
            ModelState.AddModelError(string.Empty, ae.Message);
            return RedirectToPage("./Volunteer");
        }
        catch (Exception ex)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return StatusCode(500, new { error = ex.Message });
            throw;
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] Models.Volunteer model)
    {
        // Server-side validation
        if (string.IsNullOrWhiteSpace(model.EmailAddress))
            ModelState.AddModelError("EmailAddress", "Email address is required");

        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(model.EmailAddress))
            ModelState.AddModelError("EmailAddress", "Please enter a valid email address");

        if (!ModelState.IsValid)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var errors = ModelState.Where(kvp => kvp.Value.Errors.Count > 0)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray());
                return BadRequest(new { errors });
            }
            return RedirectToPage("./Volunteer");
        }

        try
        {
            var save = await _service.Update(model).ConfigureAwait(true);
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return new OkObjectResult(save);
            return RedirectToPage("./Volunteer");
        }
        catch (Exception ex)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return StatusCode(500, new { error = ex.Message });
            throw;
        }
    }

    public async Task<IActionResult> OnPostMoveAsync([FromBody] Models.Volunteer model)
    {
        if (!ModelState.IsValid)
            return RedirectToPage("./Volunteer");

        var staff = new Models.Staff
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

        var createStaff = await _db.Staffs.AddAsync(staff).ConfigureAwait(false);
        var save = await _db.SaveChangesAsync().ConfigureAwait(false);
        if (save > 0)
        {
            var move = await _db.Clockings.Where(e => e.VoluntId == model.VolunteerId).ToListAsync()
                .ConfigureAwait(false);
            if (move != null!)
            {
                foreach (var item in move)
                {
                    var clockings = new Models.ClockingStaff
                    {
                        StafId = createStaff.Entity.StaffId,
                        ClockInTime = item.ClockInTime,
                        ClockOutTime = item.ClockOutTime,
                        LeaveOnBreakTime = item.LeaveOnBreakTime,
                        ReturnOnBreakTime = item.ReturnOnBreakTime,
                        WorkingHours = item.WorkingHours,
                        CreatedAt = item.CreatedAt
                    };

                    await _db.ClockingsStaff.AddAsync(clockings).ConfigureAwait(true);
                    var saved = await _db.SaveChangesAsync().ConfigureAwait(true);
                    if (saved > 0)
                    {
                        _db.Clockings.Remove(item);
                        await _db.SaveChangesAsync();
                    }
                }
            }
        }
        else { return RedirectToPage("./Volunteer"); }

        _db.Volunteers.Remove(model);
        await _db.SaveChangesAsync().ConfigureAwait(true);

        return RedirectToPage("./Volunteer");
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] Models.Volunteer model)
    {
        if (!ModelState.IsValid)
            return RedirectToPage("./Volunteer");

        /*_db.Volunteers.Remove(model);
        await _db.SaveChangesAsync().ConfigureAwait(true);*/

        return RedirectToPage("./Volunteer");
    }
}
