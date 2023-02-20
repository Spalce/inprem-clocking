using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Models.Identity;

public class AppUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Type { get; set; }
}
