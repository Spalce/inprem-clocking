using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class AuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ICurrentTenantService _currentTenant;

    public AuthService(UserManager<AppUser> userManager, ICurrentTenantService currentTenant)
    {
        _userManager = userManager;
        _currentTenant = currentTenant;
    }

    public async Task<IEnumerable<AppUser>> GetAll()
    {
        return await _userManager.Users.ToListAsync().ConfigureAwait(false);
    }

    public async Task<Models.PagedResult<AppUser>> GetPaged(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _userManager.Users.OrderBy(u => u.Email);

        var total = await query.CountAsync().ConfigureAwait(false);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync().ConfigureAwait(false);

        return new Models.PagedResult<AppUser>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Creates a new back-office admin account and grants it the Admin role.
    /// Only reachable from the admin-only /User page - this is the sole way new
    /// admin accounts get created now that public self-registration is closed.
    /// </summary>
    public async Task<IdentityResult> CreateAdmin(string email, string firstName, string lastName, string password)
    {
        // Set explicitly (not left for ApplicationDbContext's auto-stamp) because
        // TenantAwareUserValidator needs the real TenantId at validation time, which runs
        // before the SaveChanges call where auto-stamping would otherwise happen - by then it
        // would be too late to scope the uniqueness check to the right tenant.
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            Type = IdentitySeeder.AdminRole,
            TenantId = _currentTenant.TenantId,
        };

        var result = await _userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, IdentitySeeder.AdminRole).ConfigureAwait(false);
        }

        return result;
    }
}
