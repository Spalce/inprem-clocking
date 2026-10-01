using InpremClockingApp.Models;
using InpremClockingApp.Models.Billing;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<AppUser, AppRole, string>
    {
        private readonly ICurrentTenantService _currentTenant;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService currentTenant)
            : base(options)
        {
            _currentTenant = currentTenant;
        }

        public virtual DbSet<Clocking> Clockings { get; set; } = null!;
        public virtual DbSet<ClockingStaff> ClockingsStaff { get; set; } = null!;
        public virtual DbSet<Staff> Staffs { get; set; } = null!;
        public virtual DbSet<Volunteer> Volunteers { get; set; } = null!;
        public virtual DbSet<Setting> Setting { get; set; } = null!;
        public virtual DbSet<Tenant> Tenants { get; set; } = null!;
        public virtual DbSet<Subscription> Subscriptions { get; set; } = null!;
        public virtual DbSet<Invoice> Invoices { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Multi-tenancy Phase 0 (see multi-tenancy.md): FK relationships to Tenant are
            // configured here, without a Tenant navigation property on each domain model, since
            // nothing outside the query filter below ever needs to navigate from a row to its
            // tenant. Restrict (not cascade) delete, since deleting a tenant should never be a
            // side effect of some other change - it isn't a supported operation yet at all.
            builder.Entity<Staff>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Volunteer>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ClockingStaff>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Clocking>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Setting>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<AppUser>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);

            // NOTE on admin-account uniqueness (code review, 2026-10-01): AppUser deliberately
            // keeps Identity's default GLOBAL-unique index on NormalizedUserName, unlike
            // Staff/Volunteer's tenant-scoped uniqueness (Phase 1b). This was tried and reverted
            // - see the commit history for the full story. In short: SignInManager resolves an
            // account at login by username ALONE (UserManager.FindByNameAsync, with no tenant
            // selector anywhere in the login form), via an unordered `TOP(1)` query - allowing
            // two tenants to share a username makes login pick an arbitrary one of the matching
            // accounts, silently authenticating the wrong tenant's admin or failing with
            // "Invalid login attempt" depending on which row the query happens to return.
            // Fixing this properly would mean adding a real tenant-selection step to login
            // (e.g. an organization code), which is a product decision and a bigger change than
            // this hardening pass - out of scope here. Global uniqueness is the correct,
            // necessary behavior given today's login flow, not a bug.

            // Multi-tenancy Part 2, Phase 6 (see multi-tenancy.md): billing tables join the same
            // tenant-scoping mechanism as every other business table - no new isolation
            // primitive. Invoice also FKs to Subscription directly, since an invoice always
            // belongs to exactly one subscription period.
            builder.Entity<Subscription>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Invoice>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Invoice>().HasOne<Subscription>().WithMany().HasForeignKey(e => e.SubscriptionId).OnDelete(DeleteBehavior.Restrict);

            // Enums stored as strings, not the EF default int, so they stay legible when
            // inspected directly in SSMS (e.g. during manual billing fixes) instead of showing
            // up as bare numbers.
            builder.Entity<Subscription>().Property(e => e.BillingCycle).HasConversion<string>().HasMaxLength(20);
            builder.Entity<Subscription>().Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            builder.Entity<Invoice>().Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            builder.Entity<Subscription>().Property(e => e.Amount).HasPrecision(10, 2);
            builder.Entity<Invoice>().Property(e => e.Amount).HasPrecision(10, 2);

            // Backs BillingService.GenerateInvoiceAsync's idempotency check ("does a non-void
            // invoice already exist for this period") with a real DB constraint, the same
            // defense-in-depth every other one-row-per-tenant invariant in this schema has
            // (Subscription, Setting). Filtered (not a plain unique index) because voiding a
            // mistaken invoice must allow a correct one to be generated for the same period
            // afterward - only non-Void rows need to be unique per period.
            builder.Entity<Invoice>()
                .HasIndex(i => new { i.SubscriptionId, i.PeriodStart })
                .IsUnique()
                .HasFilter("[Status] <> 'Void'")
                .HasDatabaseName("ActiveInvoicePerPeriodIndex");

            // Multi-tenancy Phase 2 (see multi-tenancy.md, decision #3): the actual isolation
            // mechanism. Applied automatically to every query against these DbSets, everywhere
            // in the app - a future `_db.Staffs.Where(...)` call cannot accidentally leak
            // another tenant's rows, because this filter applies before that Where() runs.
            builder.Entity<Staff>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Volunteer>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<ClockingStaff>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Clocking>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Setting>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Subscription>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Invoice>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);

            // A tenant-scoped user can only ever see their own Tenant row (defensive - nothing
            // reads this yet, but a future "Organization Settings" page would). SuperAdmin
            // tenant-management (Phase 4) bypasses this deliberately via IgnoreQueryFilters().
            builder.Entity<Tenant>().HasQueryFilter(t => t.Id == _currentTenant.TenantId);

            // AppUser needs a different shape of filter than the tables above: at login time,
            // before anyone is authenticated, CurrentTenantService.TenantId is null - if this
            // filtered down to "TenantId == null" like the others, FindByEmailAsync would find
            // zero users and every login would break. So: no filtering until a tenant is known
            // (matches today's single-tenant behavior for that anonymous lookup), then scoped
            // once it is - e.g. the "Admins" list can never show another tenant's admins.
            builder.Entity<AppUser>().HasQueryFilter(u =>
                _currentTenant.TenantId == null || u.TenantId == _currentTenant.TenantId);
        }

        // Multi-tenancy Phase 2: stamps TenantId onto any newly-added row that has one, from
        // the signed-in user's own tenant, so every existing Create/Add call site in the app
        // (StaffService, VolunteerService, StaffClockingService, VolunteerClockingService,
        // Identity's UserManager.CreateAsync, etc.) needs no manual change - see
        // multi-tenancy.md decision #3. Only stamps when the value is still at its default
        // (unset), so an explicit value set by future tenant-onboarding code (Phase 4) is
        // never clobbered.
        private void StampTenantId()
        {
            var tenantId = _currentTenant.TenantId;
            if (!tenantId.HasValue) return;

            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Added) continue;

                var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "TenantId");
                if (property == null) continue;

                var isUnset = property.CurrentValue switch
                {
                    null => true,
                    int intValue => intValue == 0,
                    _ => false,
                };

                if (isUnset)
                {
                    property.CurrentValue = tenantId.Value;
                }
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampTenantId();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampTenantId();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}
