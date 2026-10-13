using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Helpers;

/// <summary>
/// Ensures the "Admin" and "SuperAdmin" roles exist. Safe to run on every startup.
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

    // Platform-operator role used by the original tenant-onboarding pages (Pages/Platform, replaced
    // by the Provider Console in multi-tenancy Phase 1) - not scoped to any one tenant
    // (AppUser.TenantId stays null), and never assignable through the ordinary /User "Create Admin" flow.
    public const string SuperAdminRole = "SuperAdmin";

    // The configuration parameter is kept so Program.cs needs no change; nothing is read from it.
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
    }
}
