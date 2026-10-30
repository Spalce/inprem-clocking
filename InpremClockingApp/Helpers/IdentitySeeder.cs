using InpremClockingApp.Models.Identity;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Helpers;

/// <summary>
/// Ensures roles and the optional configured Provider test account exist. Safe to run on every startup.
///
/// C2-5 (multi-tenancy Phase 0 step 0.6): it no longer creates any account. Until then a first
/// admin (SeedAdmin) and a platform operator (SeedSuperAdmin) were created from an email and a
/// password held in configuration - credentials in configuration, and an account nobody invited.
/// Existing databases already have their admins; a new deployment gets its first account only
/// through the provider bootstrap (one-time code, invitation, no password in configuration -
/// step 0.7), and every other account through the Admins page or an invitation.
/// </summary>
public static class IdentitySeeder
{
    public const string AdminRole = "Admin";

    // Platform-operator role of the original tenant-onboarding pages (Pages/Platform, removed in
    // multi-tenancy Phase 1 step 1.7: the Provider Console replaces them). Kept so existing accounts
    // and role rows stay valid; such accounts are refused on the App site's login.
    public const string SuperAdminRole = "SuperAdmin";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();

        if (!await roleManager.RoleExistsAsync(AdminRole))
        {
            await roleManager.CreateAsync(new AppRole { Name = AdminRole, Description = "Full back-office access" });
        }

        if (!await roleManager.RoleExistsAsync(SuperAdminRole))
        {
            await roleManager.CreateAsync(new AppRole { Name = SuperAdminRole, Description = "Platform operator - creates and manages tenants" });
        }

        var seed = configuration.GetSection("Provider:Seed");
        if (!seed.GetValue<bool>("Enabled")) return;

        var email = seed["Username"]?.Trim();
        var password = seed["TemporaryPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Provider:Seed:Username and Provider:Seed:TemporaryPassword are required when Provider:Seed:Enabled is true.");

        var users = services.GetRequiredService<UserManager<AppUser>>();
        var platform = services.GetRequiredService<PlatformDbContext>();
        var user = await users.FindByEmailAsync(email);
        if (user == null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = true,
                TenantId = null,
                RequiresPasswordChange = true,
            };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        var provider = await platform.ProviderUsers.FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (provider == null)
        {
            provider = ProviderUser.Invite(user.Id, ProviderRole.Owner, DateTime.UtcNow);
            provider.Activate();
            platform.ProviderUsers.Add(provider);
            await platform.SaveChangesAsync();
        }
        else if (provider.Status == ProviderUserStatus.Invited)
        {
            provider.Activate();
            await platform.SaveChangesAsync();
        }
    }
}
