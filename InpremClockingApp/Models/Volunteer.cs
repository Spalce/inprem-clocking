using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace InpremClockingApp.Models;

public class Volunteer
{
    [Key]
    public long VolunteerId { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Email address is required")]
    [DataType(DataType.EmailAddress, ErrorMessage = "Please enter a valid email address")]
    [DisplayName("Email Address")]
    [StringLength(100)]
    public string EmailAddress { get; set; } = string.Empty;

    [Required]
    [DisplayName("First Name")]
    [StringLength(100)]
    [RegularExpression("^[A-Za-z]+(?: +[A-Za-z]+)*$", ErrorMessage = "Only alphabets are allowed")]
    public string? FirstName { get; set; }

    [Required]
    [DisplayName("Last Name")]
    [StringLength(100)]
    [RegularExpression("^[A-Za-z]+(?: +[A-Za-z]+)*$", ErrorMessage = "Only alphabets are allowed")]
    public string? LastName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Enter a Zip Code")]
    public string? ZipCode { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Select Gender")]
    public char Gender { get; set; }

    public string? Type { get; set; }

    [Required]
    [DisplayName("Phone Number")]
    [MinLength(10, ErrorMessage = "Phone Number cannot be less than 10 digits")]
    [MaxLength(20, ErrorMessage = "Phone Number length should not be more than 20")]
    public string? PhoneNumber { get; set; }


    [DisplayName("Address")]
    [MinLength(10, ErrorMessage = "Address cannot be less than 10 characters")]
    [MaxLength(255, ErrorMessage = "Address length should not be more than 255 characters")]
    public string? Address { get; set; }

    // Volunteer sign-up questionnaire: which category the volunteer falls under, plus the
    // conditional follow-up answers for that category. Left unattributed (no [Required]) because
    // this type is also bound by the admin Manage Volunteers panel (Volunteer.cshtml.cs), which
    // never collects these fields - required-ness is enforced where the questionnaire actually
    // lives (VolunteerAttendance.cshtml.cs), not on the shared model.
    [DisplayName("Volunteer Category")]
    [StringLength(100)]
    public string? VolunteerCategory { get; set; }

    [DisplayName("Mandate Type")]
    [StringLength(100)]
    public string? MandateType { get; set; }

    [DisplayName("Name of Institution")]
    [StringLength(200)]
    public string? InstitutionName { get; set; }

    [DisplayName("Place of Work")]
    [StringLength(200)]
    public string? PlaceOfWork { get; set; }

    [DisplayName("Contact Person")]
    [StringLength(200)]
    public string? ContactPerson { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? FullName => $"{FirstName} {LastName}";

    [JsonIgnore]
    public virtual ICollection<Clocking>? Clockings { get; set; }
}

/// <summary>Canonical volunteer category values, shared between the sign-up page and its server-side validation.</summary>
public static class VolunteerCategories
{
    public const string MandatedCommunityHours = "Mandated Community Hours";
    public const string MofcVolunteerHub = "MOFC Volunteer Hub";
    public const string EducationalPurposes = "Educational Purposes";
    public const string CorporateVolunteering = "Corporate Volunteering";
    public const string PersonalOrIndividualVolunteering = "Personal or Individual Volunteering";
}

/// <summary>Sub-options under the "Mandated Community Hours" volunteer category.</summary>
public static class VolunteerMandateTypes
{
    public const string CourtOrders = "Court orders";
    public const string Diversion = "Diversion";
    public const string YouthDetention = "Youth Detention";
}
