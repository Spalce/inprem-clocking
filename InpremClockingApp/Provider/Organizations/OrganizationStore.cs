using System.Data.Common;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Organizations;
using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Subscription = Inprem.Platform.Domain.Billing.Subscription;

namespace InpremClockingApp.Provider.Organizations;

/// <summary>
/// Organizations for the Provider Console (step 1.2). Reads across the approved app's tables
/// (dbo, every organization: query filters bypassed on purpose, including the one on Tenants)
/// and the platform's.
/// Creating and editing an organization touch both, in one transaction on one connection.
/// </summary>
public sealed class OrganizationStore : IOrganizationStore
{
    private readonly ApplicationDbContext _app;
    private readonly PlatformDbContext _platform;
    private readonly UserManager<AppUser> _users;

    public OrganizationStore(ApplicationDbContext app, PlatformDbContext platform, UserManager<AppUser> users)
    {
        _app = app;
        _platform = platform;
        _users = users;
    }

    // ---------------------------------------------------------------- reading

    public async Task<IReadOnlyList<OrganizationSummary>> ListAsync(CancellationToken ct = default)
    {
        var tenants = await _app.Tenants.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var profiles = await _platform.TenantProfiles.AsNoTracking().ToDictionaryAsync(p => p.TenantId, ct);
        var subscriptions = await _platform.Subscriptions.AsNoTracking().ToDictionaryAsync(s => s.TenantId, ct);
        var planNames = await _platform.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var people = await PeopleCountsAsync(ct);
        var activity = await LastActivityAsync(ct);
        var owners = await OwnerEmailsAsync(null, ct);

        return tenants.Select(t =>
        {
            profiles.TryGetValue(t.Id, out var profile);
            subscriptions.TryGetValue(t.Id, out var subscription);
            var planId = subscription?.PlanId;
            return new OrganizationSummary(
                t.Id, t.Name, profile?.Slug ?? DefaultSlug(t.Id), profile?.Status ?? (t.IsActive ? OrganizationStatus.Active : OrganizationStatus.Suspended),
                planId, planId != null && planNames.TryGetValue(planId.Value, out var name) ? name : null,
                people.GetValueOrDefault(t.Id), activity.GetValueOrDefault(t.Id), t.CreatedAt, owners.GetValueOrDefault(t.Id) ?? new List<string>());
        }).ToList();
    }

    public async Task<OrganizationDetail?> GetAsync(int tenantId, CancellationToken ct = default)
    {
        var tenant = await _app.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant == null) return null;

        var profile = await _platform.TenantProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        var subscription = await _platform.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        Plan? plan = subscription?.PlanId == null ? null : await _platform.Plans.AsNoTracking().Include(p => p.Prices).FirstOrDefaultAsync(p => p.Id == subscription.PlanId, ct);
        var price = plan?.Prices.FirstOrDefault(p => p.Id == subscription!.PlanPriceId);

        var staff = await _app.Staffs.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, ct);
        var volunteers = await _app.Volunteers.IgnoreQueryFilters().CountAsync(v => v.TenantId == tenantId, ct);
        var activity = (await LastActivityAsync(ct)).GetValueOrDefault(tenantId);
        var owners = (await OwnerEmailsAsync(tenantId, ct)).GetValueOrDefault(tenantId) ?? new List<string>();

        var memberships = await _platform.TenantMemberships.AsNoTracking().Where(m => m.TenantId == tenantId).ToListAsync(ct);
        var userIds = memberships.Select(m => m.UserId).ToList();
        var accounts = await _app.Users.IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.TwoFactorEnabled, HasPassword = u.PasswordHash != null })
            .ToDictionaryAsync(u => u.Id, ct);
        var admins = memberships
            .Where(m => accounts.ContainsKey(m.UserId))
            .Select(m =>
            {
                var a = accounts[m.UserId];
                return new AdminView(m.UserId, a.Email ?? "", $"{a.FirstName} {a.LastName}".Trim(), m.Role, m.Status, m.LastSignInUtc, a.TwoFactorEnabled, a.HasPassword);
            })
            .OrderBy(a => a.Role).ThenBy(a => a.Email).ToList();

        var notes = await _platform.TenantNotes.AsNoTracking().Where(n => n.TenantId == tenantId).OrderByDescending(n => n.CreatedUtc).ToListAsync(ct);
        var audit = await _platform.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenantId).OrderByDescending(a => a.Id).Take(100).ToListAsync(ct);

        var summary = new OrganizationSummary(
            tenant.Id, tenant.Name, profile?.Slug ?? DefaultSlug(tenant.Id), profile?.Status ?? (tenant.IsActive ? OrganizationStatus.Active : OrganizationStatus.Suspended),
            plan?.Id, plan?.Name, staff + volunteers, activity, tenant.CreatedAt, owners);
        var subscriptionView = subscription == null ? null
            : new SubscriptionView(plan?.Name, price == null ? null : PlanService.Describe(price), subscription.Mode, subscription.Status, subscription.TrialEndsUtc, subscription.CreatedUtc);

        return new OrganizationDetail(summary, tenant.TimeZoneId, tenant.Address, tenant.ContactInfo, profile?.StatusReason, profile?.StatusChangedUtc,
            staff, volunteers, subscriptionView, admins, notes, audit);
    }

    public async Task<bool> SlugTakenAsync(string slug, int? exceptTenantId, CancellationToken ct = default)
    {
        if (await _platform.TenantProfiles.AnyAsync(p => p.Slug == slug && p.TenantId != exceptTenantId, ct)) return true;

        // Organizations without a profile yet use org-{id}.
        return slug.StartsWith("org-", StringComparison.Ordinal) && int.TryParse(slug[4..], out var id) && id != exceptTenantId
            && await _app.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == id, ct) && !await _platform.TenantProfiles.AnyAsync(p => p.TenantId == id, ct);
    }

    private static string DefaultSlug(int tenantId) => $"org-{tenantId}";

    private async Task<Dictionary<int, int>> PeopleCountsAsync(CancellationToken ct)
    {
        var staff = await _app.Staffs.IgnoreQueryFilters().GroupBy(s => s.TenantId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var volunteers = await _app.Volunteers.IgnoreQueryFilters().GroupBy(v => v.TenantId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return staff.Concat(volunteers).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
    }

    /// <summary>The latest clocking session or admin sign-in per organization.</summary>
    private async Task<Dictionary<int, DateTime?>> LastActivityAsync(CancellationToken ct)
    {
        var staff = await _app.ClockingsStaff.IgnoreQueryFilters().GroupBy(c => c.TenantId).Select(g => new { g.Key, Last = g.Max(c => c.CreatedAt) }).ToListAsync(ct);
        var volunteers = await _app.Clockings.IgnoreQueryFilters().GroupBy(c => c.TenantId).Select(g => new { g.Key, Last = g.Max(c => c.CreatedAt) }).ToListAsync(ct);
        var signIns = await _platform.TenantMemberships.GroupBy(m => m.TenantId).Select(g => new { g.Key, Last = g.Max(m => m.LastSignInUtc) }).ToListAsync(ct);
        return staff.Concat(volunteers).Concat(signIns).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Max(x => x.Last));
    }

    private async Task<Dictionary<int, List<string>>> OwnerEmailsAsync(int? tenantId, CancellationToken ct)
    {
        var rows = await (from m in _platform.TenantMemberships.AsNoTracking()
                          join a in _platform.Accounts.AsNoTracking() on m.UserId equals a.Id
                          where m.Role == MembershipRole.Owner && m.Status != MembershipStatus.Revoked && (tenantId == null || m.TenantId == tenantId)
                          select new { m.TenantId, a.Email }).ToListAsync(ct);
        return rows.GroupBy(r => r.TenantId).ToDictionary(g => g.Key, g => g.Select(r => r.Email ?? "").ToList());
    }

    // ---------------------------------------------------------------- writing

    public async Task<ProvisionedOrganization> ProvisionAsync(OrganizationProvisioning provisioning, CancellationToken ct = default)
    {
        var input = provisioning.Organization;
        var now = provisioning.NowUtc;

        await using var transaction = await _app.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            Name = input.Name, TimeZoneId = input.TimeZoneId, Address = input.Address, ContactInfo = input.ContactInfo,
            IsActive = true, CreatedAt = now,
        };
        _app.Tenants.Add(tenant);
        await _app.SaveChangesAsync(ct);

        // No row in the old dbo.Subscriptions any more (Phase 2 step 2.9): the platform subscription,
        // added below in the same transaction, is the only billing record.

        var ownerId = provisioning.ExistingOwnerUserId;
        var isNew = ownerId == null;
        if (isNew)
        {
            var owner = new AppUser
            {
                UserName = input.OwnerEmail, Email = input.OwnerEmail, EmailConfirmed = false, LockoutEnabled = true,
                FirstName = input.OwnerFirstName, LastName = input.OwnerLastName, PhoneNumber = input.OwnerMobile,
                Type = IdentitySeeder.AdminRole, TenantId = tenant.Id,
            };
            Check(await _users.CreateAsync(owner), "create the owner's account");
            ownerId = owner.Id;
        }

        var account = await _users.FindByIdAsync(ownerId!) ?? throw new InvalidOperationException("The owner's account was not found.");
        if (!await _users.IsInRoleAsync(account, IdentitySeeder.AdminRole)) Check(await _users.AddToRoleAsync(account, IdentitySeeder.AdminRole), "give the owner the Admin role");

        await using var platform = SharedPlatformContext(_app.Database.GetDbConnection());
        await platform.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);

        Plan? plan = input.PlanId == null ? null : await platform.Plans.Include(p => p.Prices).FirstAsync(p => p.Id == input.PlanId, ct);
        var price = plan?.Prices.First(p => p.Id == input.PlanPriceId);
        var subscription = Subscription.Start(tenant.Id, plan, price, input.Mode, input.TrialDays, now);

        platform.TenantProfiles.Add(TenantProfile.Create(tenant.Id, input.Slug, now));
        platform.Subscriptions.Add(subscription);
        platform.TenantMemberships.Add(isNew
            ? TenantMembership.CreateActive(ownerId!, tenant.Id, MembershipRole.Owner, now)
            : TenantMembership.CreateInvited(ownerId!, tenant.Id, MembershipRole.Owner, provisioning.Actor, now));
        platform.AuditEntries.Add(AuditEntry.Record(provisioning.Actor, "organization.created", tenant.Id.ToString(),
            $"{input.Name} ({input.Slug}), owner {input.OwnerEmail} ({(isNew ? "new account" : "existing account, invited")}), " +
            $"{input.Mode}{(plan == null ? "" : $", plan {plan.Name}")}{(subscription.TrialEndsUtc != null ? $", trial until {subscription.TrialEndsUtc:yyyy-MM-dd}" : "")}",
            now, tenant.Id));
        await platform.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
        return new ProvisionedOrganization(tenant.Id, ownerId!, isNew);
    }

    public async Task<TenantProfile?> FindProfileAsync(int tenantId, CancellationToken ct = default)
    {
        var profile = await _platform.TenantProfiles.FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        if (profile != null) return profile;

        // An organization from before the console (or created by the old pages): its profile is made now.
        var tenant = await _app.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant == null) return null;
        profile = TenantProfile.Create(tenantId, DefaultSlug(tenantId), DateTime.SpecifyKind(tenant.CreatedAt, DateTimeKind.Utc));
        if (!tenant.IsActive) profile.Suspend("Inactive before the Provider Console", DateTime.UtcNow);
        _platform.TenantProfiles.Add(profile);
        return profile;
    }

    public async Task UpdateAsync(int tenantId, OrganizationEdit edit, AuditEntry audit, CancellationToken ct = default)
    {
        await using var transaction = await _app.Database.BeginTransactionAsync(ct);
        var tenant = await _app.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId, ct);
        tenant.Name = edit.Name;
        tenant.TimeZoneId = edit.TimeZoneId;
        tenant.Address = edit.Address;
        tenant.ContactInfo = edit.ContactInfo;
        await _app.SaveChangesAsync(ct);

        await using var platform = SharedPlatformContext(_app.Database.GetDbConnection());
        await platform.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        var profile = await platform.TenantProfiles.FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        if (profile == null) platform.TenantProfiles.Add(TenantProfile.Create(tenantId, edit.Slug, DateTime.SpecifyKind(tenant.CreatedAt, DateTimeKind.Utc)));
        else profile.ChangeSlug(edit.Slug);
        platform.AuditEntries.Add(audit);
        await platform.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<TenantMembership>> ListMembershipsAsync(int tenantId, CancellationToken ct = default) =>
        await _platform.TenantMemberships.Where(m => m.TenantId == tenantId).ToListAsync(ct);

    public async Task AddNoteAsync(TenantNote note, CancellationToken ct = default) => await _platform.TenantNotes.AddAsync(note, ct);

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _platform.AuditEntries.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _platform.SaveChangesAsync(ct);

    /// <summary>A platform context on the app context's connection, so both share one transaction.</summary>
    private static PlatformDbContext SharedPlatformContext(DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformDbContext.Schema))
            .Options;
        return new PlatformDbContext(options);
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded) throw new InvalidOperationException($"Could not {what}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
    }
}
