using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

/// <summary>
/// SuperAdmin-only tenant onboarding (see multi-tenancy.md Phase 4). Reachable only from
/// /Platform/Tenants, gated by the SuperAdminOnly policy - nowhere else in the app creates a
/// Tenant row or an AppUser with an explicit TenantId.
/// </summary>
public class TenantAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public TenantAdminService(ApplicationDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    // A SuperAdmin has no TenantId, so the Tenant query filter (t.Id == current tenant) would
    // otherwise hide every tenant, including the one just created - IgnoreQueryFilters() is the
    // deliberate, audited bypass described in multi-tenancy.md decision #3/#6.
    public async Task<List<Tenant>> GetAllTenantsAsync()
    {
        return await _db.Tenants.IgnoreQueryFilters()
            .OrderBy(t => t.Name)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<IdentityResult> CreateTenantWithAdminAsync(
        string tenantName, string timeZoneId, string adminEmail, string adminFirstName, string adminLastName, string adminPassword)
    {
        var tenant = new Tenant
        {
            Name = tenantName,
            TimeZoneId = timeZoneId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        await _db.Tenants.AddAsync(tenant).ConfigureAwait(false);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        // The calling SuperAdmin has no TenantId of their own for the usual auto-stamp
        // (ApplicationDbContext.SaveChanges) to copy onto this new user, so it's set explicitly
        // here - the one place in the app that assigns a TenantId other than a user's own.
        var admin = new AppUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FirstName = adminFirstName,
            LastName = adminLastName,
            Type = IdentitySeeder.AdminRole,
            TenantId = tenant.Id,
        };

        var result = await _userManager.CreateAsync(admin, adminPassword).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(admin, IdentitySeeder.AdminRole).ConfigureAwait(false);
        }
        else
        {
            // Roll back the tenant too - a tenant with no admin able to sign into it is dead
            // weight, and retrying the whole form is simpler than a partially-onboarded tenant.
            _db.Tenants.Remove(tenant);
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }

        return result;
    }
}
