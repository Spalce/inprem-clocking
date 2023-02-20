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
}
