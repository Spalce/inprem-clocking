using InpremClockingApp.Data;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InpremClockingApp.Helpers;

/// <summary>
/// Ensures the "Admin" and "SuperAdmin" roles exist and that a first admin account is
/// provisioned from configuration (SeedAdmin:Email / SeedAdmin:Password), so a fresh deployment
/// always has at least one working admin login without ever hardcoding credentials in source.
/// Safe to run on every startup: it does nothing once an admin account already exists.
/// </summary>
public static class IdentitySeeder
{
    public const string AdminRole = "Admin";

    // Platform-operator role for tenant onboarding (multi-tenancy.md Phase 4/decision #6) -
    // not scoped to any one tenant (AppUser.TenantId stays null for these accounts), and never
    // assignable through the ordinary /User "Create Admin" flow.
    public const string SuperAdminRole = "SuperAdmin";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");
        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        if (!await roleManager.RoleExistsAsync(AdminRole))
        {
            await roleManager.CreateAsync(new AppRole { Name = AdminRole, Description = "Full back-office access" });
        }

        if (!await roleManager.RoleExistsAsync(SuperAdminRole))
        {
            await roleManager.CreateAsync(new AppRole { Name = SuperAdminRole, Description = "Platform operator - creates and manages tenants" });
        }

        await SeedSuperAdminAsync(services, configuration, logger, userManager);

        var email = configuration["SeedAdmin:Email"];
        var password = configuration["SeedAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("SeedAdmin:Email / SeedAdmin:Password are not configured - skipping admin seeding. " +
                               "Set them (e.g. via environment variables) to provision the first admin account.");
            return;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            // This seeder runs at startup, outside any HTTP request, so there's no signed-in
            // user for ApplicationDbContext's TenantId auto-stamp to key off - the new admin's
            // tenant has to be set explicitly here instead. IgnoreQueryFilters() is required
            // for the same reason (CurrentTenantService.TenantId is null this early, which
            // would otherwise filter the Tenants table down to nothing). Assigns to the first
            // tenant that exists; once tenant onboarding (multi-tenancy.md Phase 4) exists,
            // that flow creates a tenant and its first admin together instead of relying on
            // this generic seeder.
            var db = services.GetRequiredService<ApplicationDbContext>();
            var firstTenant = await db.Tenants.IgnoreQueryFilters().OrderBy(t => t.Id).FirstOrDefaultAsync();

            user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Type = AdminRole,
                TenantId = firstTenant?.Id,
            };

            var create = await userManager.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                logger.LogError("Failed to seed admin account {Email}: {Errors}", email,
                    string.Join("; ", create.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Seeded initial admin account {Email}.", email);
        }

        if (!await userManager.IsInRoleAsync(user, AdminRole))
        {
            await userManager.AddToRoleAsync(user, AdminRole);
        }
    }

    // Mirrors the SeedAdmin pattern above, but this account is intentionally never given a
    // TenantId - a SuperAdmin operates outside every tenant's data, not inside one of them.
    // Optional: without SeedSuperAdmin:Email/Password configured, tenant onboarding (Phase 4)
    // simply isn't reachable yet, which is a fine default until someone needs it.
    private static async Task SeedSuperAdminAsync(
        IServiceProvider services, IConfiguration configuration, ILogger logger, UserManager<AppUser> userManager)
    {
        var email = configuration["SeedSuperAdmin:Email"];
        var password = configuration["SeedSuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Type = SuperAdminRole,
            };

            var create = await userManager.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                logger.LogError("Failed to seed super-admin account {Email}: {Errors}", email,
                    string.Join("; ", create.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Seeded initial super-admin account {Email}.", email);
        }

        if (!await userManager.IsInRoleAsync(user, SuperAdminRole))
        {
            await userManager.AddToRoleAsync(user, SuperAdminRole);
        }
    }
}
