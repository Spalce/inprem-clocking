using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Billing;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

// A tenant paired with its subscription for the /Platform/Tenants list (multi-tenancy.md Part 2,
// Phase 7) - no EF navigation property exists between the two (same "no nav property, manual
// join" pattern as every other tenant-scoped entity), so this is assembled in
// GetAllTenantsWithSubscriptionsAsync rather than queried directly.
public class TenantListItem
{
    public Tenant Tenant { get; set; } = null!;
    public Subscription? Subscription { get; set; }
}

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
    // deliberate, audited bypass described in multi-tenancy.md decision #3/#6. Same reasoning
    // applies to Subscriptions here (Phase 7) - a SuperAdmin needs every tenant's billing status
    // for the list badge, not just their own (they have none).
    public async Task<List<TenantListItem>> GetAllTenantsWithSubscriptionsAsync()
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .OrderBy(t => t.Name)
            .ToListAsync().ConfigureAwait(false);

        var subscriptionsByTenant = await _db.Subscriptions.IgnoreQueryFilters()
            .ToDictionaryAsync(s => s.TenantId).ConfigureAwait(false);

        return tenants.Select(t => new TenantListItem
        {
            Tenant = t,
            Subscription = subscriptionsByTenant.GetValueOrDefault(t.Id),
        }).ToList();
    }

    public async Task<Tenant?> GetTenantByIdAsync(int id)
    {
        return await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id).ConfigureAwait(false);
    }

    // Any AppUser with this TenantId is necessarily an Admin of it - SuperAdmin accounts always
    // have a null TenantId (see AppUser.cs), and nothing else in the app assigns one. The
    // AppUser query filter itself doesn't need IgnoreQueryFilters() here: it only restricts a
    // tenant-scoped caller to their own tenant, and passes through unfiltered when the caller
    // (a SuperAdmin) has no TenantId at all - see ApplicationDbContext.OnModelCreating.
    public async Task<List<AppUser>> GetAdminsForTenantAsync(int tenantId)
    {
        return await _db.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task UpdateTenantDetailsAsync(int id, string name, string timeZoneId, string? address, string? contactInfo)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id).ConfigureAwait(false);
        if (tenant == null) return;

        tenant.Name = name;
        tenant.TimeZoneId = timeZoneId;
        tenant.Address = address;
        tenant.ContactInfo = contactInfo;
        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task SetTenantActiveAsync(int id, bool isActive)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id).ConfigureAwait(false);
        if (tenant == null) return;

        tenant.IsActive = isActive;
        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<Subscription?> GetSubscriptionForTenantAsync(int tenantId)
    {
        return await _db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId).ConfigureAwait(false);
    }

    // Editing only, for now - generating the next period's invoice and the renewal/grace-period
    // mechanics around this are Phase 8's BillingService, not this one (multi-tenancy.md Part 2).
    public async Task UpdateSubscriptionAsync(
        int tenantId, decimal amount, BillingCycle billingCycle, SubscriptionStatus status,
        DateTime currentPeriodStart, DateTime currentPeriodEnd)
    {
        var subscription = await _db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId).ConfigureAwait(false);
        if (subscription == null) return;

        subscription.Amount = amount;
        subscription.BillingCycle = billingCycle;
        subscription.Status = status;
        subscription.CurrentPeriodStart = currentPeriodStart;
        subscription.CurrentPeriodEnd = currentPeriodEnd;
        await _db.SaveChangesAsync().ConfigureAwait(false);
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

        // Every tenant gets a subscription row from the moment it exists - same placeholder
        // shape the Phase 6 data migration seeded for every pre-existing tenant, so there's
        // never a tenant with undefined billing state. The real amount gets set afterward via
        // the Phase 7 tenant detail page; this just guarantees the row exists.
        var subscription = new Subscription
        {
            TenantId = tenant.Id,
            Amount = 0,
            Currency = "USD",
            BillingCycle = BillingCycle.Monthly,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddYears(1),
            CreatedAt = DateTime.UtcNow,
        };
        await _db.Subscriptions.AddAsync(subscription).ConfigureAwait(false);
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
            // Roll back the subscription and tenant too - a tenant with no admin able to sign
            // into it is dead weight, and retrying the whole form is simpler than a
            // partially-onboarded tenant. Subscription first: Invoice/Subscription FK to Tenant
            // with Restrict delete, so Tenant can't be removed while it still exists.
            _db.Subscriptions.Remove(subscription);
            _db.Tenants.Remove(tenant);
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }

        return result;
    }
}
