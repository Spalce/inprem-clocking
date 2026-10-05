using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
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
    private readonly ICurrentTenantProfile _tenantProfile;

    public Staff(StaffService service, ApplicationDbContext db, ICurrentTenantProfile tenantProfile)
    {
        _service = service;
        _db = db;
        _tenantProfile = tenantProfile;
    }

    public IEnumerable<Models.Staff>? Staffs { get; set; }
    public Models.Staff? Model { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    // Named "pageNumber" rather than "page" because "page" is a reserved Razor Pages route
    // value (the page's own path) - a query string "page" is intercepted by route-value model
    // binding before it ever reaches a same-named property.
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    // Posted (as a query string flag, since the request body is the JSON staff payload) once the
    // admin has already seen - and dismissed - the same-name confirmation on OnPostCreateAsync.
    [BindProperty(SupportsGet = true)]
    public bool ConfirmDifferentPerson { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await _service.SearchByName(Search, PageNumber, PageSize).ConfigureAwait(true);
        Staffs = result.Items;

        ViewData["Search"] = Search;
        ViewData["TotalCount"] = result.TotalCount;
        ViewData["Page"] = result.Page;
        ViewData["PageSize"] = result.PageSize;
        ViewData["TotalPages"] = result.TotalPages;

        return Page();
    }

    // Export buttons (Copy/CSV/Excel/PDF/Print) always export every row matching the current
    // Search filter, not just the one page currently on screen - int.MaxValue as the page size
    // reuses the same SearchByName query/filter logic as the list itself instead of duplicating it.
    private async Task<IEnumerable<RosterExport.Row>> GetFilteredRowsAsync()
    {
        var result = await _service.SearchByName(Search, 1, int.MaxValue).ConfigureAwait(true);
        return result.Items.Select(s => new RosterExport.Row(
            s.StaffId, s.FirstName ?? "", s.LastName ?? "", s.EmailAddress ?? "",
            s.Gender.ToString(), s.PhoneNumber ?? "", s.ZipCode ?? ""));
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        var bytes = RosterExport.BuildCsv(await GetFilteredRowsAsync().ConfigureAwait(true));
        return File(bytes, "text/csv", "staff.csv");
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
    {
        var bytes = RosterExport.BuildExcelHtml("Staff List", await GetFilteredRowsAsync().ConfigureAwait(true));
        return File(bytes, "application/vnd.ms-excel", "staff.xls");
    }

    public async Task<IActionResult> OnGetExportPdfAsync()
    {
        var bytes = RosterExport.BuildPdf(_tenantProfile.Name, "Staff List", await GetFilteredRowsAsync().ConfigureAwait(true));
        return File(bytes, "application/pdf", "staff.pdf");
    }

    // Backs the Copy and Print buttons, which need the full filtered row set client-side rather
    // than a downloaded file.
    public async Task<IActionResult> OnGetExportJsonAsync()
    {
        return new JsonResult(await GetFilteredRowsAsync().ConfigureAwait(true));
    }

    //public async Task<IActionResult> OnPostCreateAsync([FromBody] Models.Staff model)
    //{
    //    // 1. Validate required fields
    //    if (string.IsNullOrWhiteSpace(model.EmailAddress))
    //    {
    //        ModelState.AddModelError("EmailAddress", "Email address is required");
    //    }
    //    else if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute()
    //                 .IsValid(model.EmailAddress))
    //    {
    //        ModelState.AddModelError("EmailAddress", "Please enter a valid email address");
    //    }

    //    if (string.IsNullOrWhiteSpace(model.FirstName))
    //    {
    //        ModelState.AddModelError("FirstName", "First name is required");
    //    }

    //    if (string.IsNullOrWhiteSpace(model.LastName))
    //    {
    //        ModelState.AddModelError("LastName", "Last name is required");
    //    }

    //    // 2. Stop here if validation failed
    //    if (!ModelState.IsValid)
    //    {
    //        var errors = ModelState
    //            .Where(kvp => kvp.Value.Errors.Count > 0)
    //            .ToDictionary(
    //                kvp => kvp.Key,
    //                kvp => kvp.Value.Errors
    //                    .Select(e => e.ErrorMessage)
    //                    .ToArray());

    //        return BadRequest(new { errors });
    //    }

    //    // 3. Now check whether the email already exists
    //    var staff = await _service.GetByEmail(model.EmailAddress);

    //    if (staff != null)
    //    {
    //        ModelState.AddModelError(
    //            "EmailAddress",
    //            "A staff with this email already exists");

    //        var errors = ModelState
    //            .Where(kvp => kvp.Value.Errors.Count > 0)
    //            .ToDictionary(
    //                kvp => kvp.Key,
    //                kvp => kvp.Value.Errors
    //                    .Select(e => e.ErrorMessage)
    //                    .ToArray());

    //        return BadRequest(new { errors });
    //    }

    //    // 4. Set system-generated values
    //    model.CreatedAt = DateTime.Now;
    //    model.Type = "Staff";

    //    // 5. Save the new staff
    //    try
    //    {
    //        var save = await _service.Create(model);

    //        return new ObjectResult(save)
    //        {
    //            StatusCode = StatusCodes.Status201Created
    //        };
    //    }
    //    catch (Microsoft.EntityFrameworkCore.DbUpdateException dbex)
    //    {
    //        var msg = dbex.InnerException?.Message ?? dbex.Message;

    //        return BadRequest(new
    //        {
    //            error = "Database update failed: " + msg
    //        });
    //    }
    //    catch (ArgumentException ae)
    //    {
    //        return BadRequest(new
    //        {
    //            error = ae.Message
    //        });
    //    }
    //    catch (Exception ex)
    //    {
    //        return StatusCode(500, new
    //        {
    //            error = ex.Message
    //        });
    //    }
    //}

    public async Task<IActionResult> OnPostCreateAsync([FromBody] Models.Staff model)
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

        var staff = await _service.GetByEmail(model.EmailAddress).ConfigureAwait(true);
        if (staff != null)
        {
            ModelState.AddModelError("EmailAddress", "A staff with this email already exists");
        }

        if (!ModelState.IsValid)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var errors = ModelState.Where(kvp => kvp.Value.Errors.Count > 0)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray());
                return BadRequest(new { errors });
            }
            return RedirectToPage("./Staff");
        }

        // A same-name match (email is already confirmed clear) is only a soft signal - warn the
        // admin and let them confirm before creating, unless they already dismissed this once for
        // the current submission. Unlike the kiosk's version of this check, there's no need to
        // mask the email here - an admin already has full read access to every staff record.
        if (!ConfirmDifferentPerson && !string.IsNullOrWhiteSpace(model.FirstName) && !string.IsNullOrWhiteSpace(model.LastName))
        {
            var nameMatches = await _service.FindByFullName(model.FirstName, model.LastName).ConfigureAwait(true);
            if (nameMatches.Count > 0)
            {
                return new ObjectResult(new
                {
                    nameConflict = true,
                    matches = nameMatches.Select(s => new { id = s.StaffId, name = s.FullName, email = s.EmailAddress })
                })
                { StatusCode = StatusCodes.Status409Conflict };
            }
        }

        model.CreatedAt = DateTime.UtcNow;
        model.Type = "Staff";

        try
        {
            var save = await _service.Create(model).ConfigureAwait(true);
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new ObjectResult(save) { StatusCode = StatusCodes.Status201Created };
            }
            return RedirectToPage("./Staff");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbex)
        {
            // Return meaningful DB error for API clients
            var msg = dbex.InnerException?.Message ?? dbex.Message;
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return BadRequest(new { error = "Database update failed: " + msg });
            ModelState.AddModelError(string.Empty, "Database update failed: " + msg);
            return RedirectToPage("./Staff");
        }
        catch (ArgumentException ae)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return BadRequest(new { error = ae.Message });
            ModelState.AddModelError(string.Empty, ae.Message);
            return RedirectToPage("./Staff");
        }
        catch (Exception ex)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return StatusCode(500, new { error = ex.Message });
            throw;
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] Models.Staff model)
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
            return RedirectToPage("./Staff");
        }

        try
        {
            var save = await _service.Update(model).ConfigureAwait(true);
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return new OkObjectResult(save);
            return RedirectToPage("./Staff");
        }
        catch (Exception ex)
        {
            if (Request?.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return StatusCode(500, new { error = ex.Message });
            throw;
        }
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
