using InpremClockingApp.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace InpremClockingApp.Helpers;

// Required-ness of the volunteer category and its category-specific follow-up answers, shared by
// the kiosk sign-up form (VolunteerAttendance) and the back-office Add/Edit Volunteer dialog
// (Volunteer.cshtml.cs) so both enforce the same rules with the same wording. Kept off the
// Volunteer model's attributes because which fields are required depends on the chosen category.
// The client-side equivalents are in VolunteerAttendance.cshtml (kiosk) and
// _VolunteerEditorScript.cshtml (back office).
public static class VolunteerCategoryValidation
{
    // prefix is the ModelState key prefix of the bound volunteer: "Input." on the kiosk page,
    // "" for the back office's [FromBody] model.
    public static void Validate(Volunteer volunteer, ModelStateDictionary modelState, string prefix = "")
    {
        void Require(string? value, string property, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
                modelState.AddModelError(prefix + property, message);
        }

        if (string.IsNullOrWhiteSpace(volunteer.VolunteerCategory))
        {
            modelState.AddModelError(prefix + nameof(Volunteer.VolunteerCategory), "Select a volunteer category");
            return;
        }

        switch (volunteer.VolunteerCategory)
        {
            case VolunteerCategories.MandatedCommunityHours:
                Require(volunteer.MandateType, nameof(Volunteer.MandateType), "Please select which mandate applies");
                break;

            case VolunteerCategories.EducationalPurposes:
                Require(volunteer.InstitutionName, nameof(Volunteer.InstitutionName), "Name of institution is required");
                RequireContactPerson(volunteer, Require);
                break;

            case VolunteerCategories.CorporateVolunteering:
                Require(volunteer.PlaceOfWork, nameof(Volunteer.PlaceOfWork), "Place of work is required");
                RequireContactPerson(volunteer, Require);
                break;
        }
    }

    private static void RequireContactPerson(Volunteer volunteer, Action<string?, string, string> require)
    {
        require(volunteer.ContactPerson, nameof(Volunteer.ContactPerson), "Contact person is required");
        require(volunteer.ContactPersonPosition, nameof(Volunteer.ContactPersonPosition), "Contact person position is required");
        require(volunteer.ContactPersonEmail, nameof(Volunteer.ContactPersonEmail), "Contact person email is required");
        require(volunteer.ContactPersonPhone, nameof(Volunteer.ContactPersonPhone), "Contact person phone is required");
    }
}
