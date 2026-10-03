using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Pages;

public class VolunteerAttendance : PageModel
{
    private readonly ApplicationDbContext _db;

    public VolunteerAttendance(ApplicationDbContext db)
    {
        _db = db;
    }

    public int TimeOut { get; set; }
    [BindProperty] public Models.Volunteer? Input { get; set; }

    // Posted back (via the "No, this is a different person" button) once the user has already
    // been shown - and dismissed - the same-name confirmation, so the name check isn't repeated.
    [BindProperty] public bool ConfirmDifferentPerson { get; set; }

    // Set when a first+last name match was found; the view renders a confirmation prompt
    // instead of inserting, and preserves the originally entered Input values for resubmission.
    public bool NameConflict { get; set; }
    public long ExistingId { get; set; }
    public string? ExistingName { get; set; }

    public Task OnGetAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input == null)
            return RedirectToPage("./VolunteerAttendance");

        ValidateVolunteerCategory();

        if (!ModelState.IsValid)
            return Page();

        var email = Input.EmailAddress?.Trim();
        var phone = Input.PhoneNumber?.Trim();

        // An email or phone match is treated as the same person re-registering - block outright,
        // no confirmation needed since these are expected to be unique per person.
        var emailOrPhoneMatch = await _db.Volunteers
            .FirstOrDefaultAsync(e =>
                (email != null && e.EmailAddress.ToLower() == email.ToLower()) ||
                (phone != null && e.PhoneNumber == phone))
            .ConfigureAwait(false);

        if (emailOrPhoneMatch != null)
        {
            var matchedOnEmail = email != null &&
                string.Equals(emailOrPhoneMatch.EmailAddress, email, StringComparison.OrdinalIgnoreCase);
            TempData["Message"] = matchedOnEmail
                ? "A volunteer with this email address is already registered."
                : "A volunteer with this phone number is already registered.";
            return RedirectToPage("./VolunteerAttendance");
        }

        // A same-name match (email/phone are already confirmed clear) is only a soft signal -
        // ask the user to confirm before creating a new record, unless they already answered
        // "no, different person" on a prior submission of this same form.
        if (!ConfirmDifferentPerson)
        {
            var firstName = Input.FirstName?.Trim();
            var lastName = Input.LastName?.Trim();

            var nameMatch = await _db.Volunteers
                .FirstOrDefaultAsync(e =>
                    e.FirstName != null && e.LastName != null &&
                    e.FirstName.ToLower() == firstName!.ToLower() &&
                    e.LastName.ToLower() == lastName!.ToLower())
                .ConfigureAwait(false);

            if (nameMatch != null)
            {
                NameConflict = true;
                ExistingId = nameMatch.VolunteerId;
                ExistingName = nameMatch.FullName;
                return Page();
            }
        }

        Input.CreatedAt = DateTime.UtcNow;
        Input.Type = "Volunteer";

        try
        {
            await _db.Volunteers.AddAsync(Input).ConfigureAwait(false);
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsDuplicateEmailViolation(ex))
        {
            // Lost a race with a concurrent registration using the same email. The unique index
            // on EmailAddress is the real guarantee; the check above is just the fast path that
            // avoids hitting the constraint in the common, non-racing case.
            TempData["Message"] = "A volunteer with this email address is already registered.";
            return RedirectToPage("./VolunteerAttendance");
        }

        TempData["Message"] = "Volunteer registered successfully!";
        return Redirect($"/volunteer-clockin/{Input.VolunteerId}");
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException ex)
    {
        // SQL Server: 2601 = duplicate key on a unique index, 2627 = unique constraint violation.
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }

    // The volunteer category question and its category-specific follow-up fields are only
    // required here on the sign-up questionnaire - kept off the Volunteer model itself since
    // that type is also bound by the admin Manage Volunteers panel, which never sends these.
    private void ValidateVolunteerCategory()
    {
        if (Input == null)
            return;

        if (string.IsNullOrWhiteSpace(Input.VolunteerCategory))
        {
            ModelState.AddModelError("Input.VolunteerCategory", "Please select which volunteer category you fall under");
            return;
        }

        switch (Input.VolunteerCategory)
        {
            case VolunteerCategories.MandatedCommunityHours:
                if (string.IsNullOrWhiteSpace(Input.MandateType))
                    ModelState.AddModelError("Input.MandateType", "Please select which mandate applies");
                break;

            case VolunteerCategories.EducationalPurposes:
                if (string.IsNullOrWhiteSpace(Input.InstitutionName))
                    ModelState.AddModelError("Input.InstitutionName", "Name of institution is required");
                if (string.IsNullOrWhiteSpace(Input.ContactPerson))
                    ModelState.AddModelError("Input.ContactPerson", "Contact person is required");
                if (string.IsNullOrWhiteSpace(Input.ContactPersonPosition))
                    ModelState.AddModelError("Input.ContactPersonPosition", "Contact person position is required");
                if (string.IsNullOrWhiteSpace(Input.ContactPersonEmail))
                    ModelState.AddModelError("Input.ContactPersonEmail", "Contact person email is required");
                if (string.IsNullOrWhiteSpace(Input.ContactPersonPhone))
                    ModelState.AddModelError("Input.ContactPersonPhone", "Contact person phone is required");
                break;

            case VolunteerCategories.CorporateVolunteering:
                if (string.IsNullOrWhiteSpace(Input.PlaceOfWork))
                    ModelState.AddModelError("Input.PlaceOfWork", "Place of work is required");
                if (string.IsNullOrWhiteSpace(Input.ContactPerson))
                    ModelState.AddModelError("Input.ContactPerson", "Contact person is required");
                break;
        }
    }
}
