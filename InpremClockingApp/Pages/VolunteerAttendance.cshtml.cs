using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

        // Check if a volunteer with the same email already exists
        var existing = await _db.Volunteers
            .FirstOrDefaultAsync(e => e.EmailAddress == Input.EmailAddress)
            .ConfigureAwait(false);

        if (existing != null)
        {
            TempData["Message"] = "A volunteer with this email address is already registered.";
            return RedirectToPage("./VolunteerAttendance");
        }

        Input.CreatedAt = DateTime.UtcNow;
        Input.Type = "Volunteer";

        await _db.Volunteers.AddAsync(Input).ConfigureAwait(false);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        TempData["Message"] = "Volunteer registered successfully!";
        return Redirect($"/volunteer-clockin/{Input.VolunteerId}");
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
