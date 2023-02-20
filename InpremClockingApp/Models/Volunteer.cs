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
    public string? EmailAddress { get; set; }

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

    public DateTime? CreatedAt { get; set; }

    public string? FullName => $"{FirstName} {LastName}";

    [JsonIgnore]
    public virtual ICollection<Clocking>? Clockings { get; set; }
}
