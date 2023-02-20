using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Models.Identity;

public class AppRole : IdentityRole
{
    public string? Description { get; set; }
}
