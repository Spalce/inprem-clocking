using InpremClockingApp.Data;
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

    public Staff(StaffService service, ApplicationDbContext db)
    {
        _service = service;
        _db = db;
    }

    public IEnumerable<Models.Staff>? Staffs { get; set; }
    public Models.Staff? Model { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var model = await _service.GetAll().ConfigureAwait(true);
        if (model != null!)
            Staffs = model;
        return Page();
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
