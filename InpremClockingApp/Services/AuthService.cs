using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class AuthService
{
    private readonly UserManager<AppUser> _userManager;

    public AuthService(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IEnumerable<AppUser>> GetAll()
    {
        return await _userManager.Users.ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new back-office admin account and grants it the Admin role.
    /// Only reachable from the admin-only /User page - this is the sole way new
    /// admin accounts get created now that public self-registration is closed.
    /// </summary>
    public async Task<IdentityResult> CreateAdmin(string email, string firstName, string lastName, string password)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            Type = IdentitySeeder.AdminRole,
        };

        var result = await _userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, IdentitySeeder.AdminRole).ConfigureAwait(false);
        }

        return result;
    }
}
