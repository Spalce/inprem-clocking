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

            // Identity's own base.OnModelCreating() above configures a single global-unique
            // index on NormalizedUserName ("UserNameIndex") - loosened to non-unique here and
            // replaced with two filtered indexes, matching Staff/Volunteer's Phase 1b redesign:
            // one scoping uniqueness to each tenant, one keeping SuperAdmin accounts
            // (TenantId null) uniquely named platform-wide, since there's no tenant to scope
            // those to. Two indexes rather than one (TenantId, NormalizedUserName) composite,
            // because SQL Server's unique indexes never treat two NULLs as equal - a single
            // composite would silently stop enforcing uniqueness among SuperAdmins altogether.
            // UserName is always the admin's email in this app, so without this, two different
            // organizations couldn't have an admin sharing an email address. Calling HasIndex
            // on the plain NormalizedUserName property reconfigures Identity's existing index
            // rather than adding a second one; the two filtered ones below need explicit names
            // via the HasIndex(properties, name) overload specifically because EF Core would
            // otherwise treat them as configuring that same single-property index too.
            //
            // IMPORTANT: this alone is not a safe fix. The first attempt at this (2026-10-01)
            // was shipped with only this index + TenantAwareUserValidator and reverted within the
            // hour, because SignInManager resolves an account at login by username ALONE
            // (UserManager.FindByNameAsync, no tenant selector anywhere in the login form) via an
            // unordered `TOP(1)` query - two tenants sharing a username made login pick an
            // arbitrary one of the matching accounts. The fix that makes this safe lives in
            // Login.cshtml.cs (disambiguates by password), ForgotPassword.cshtml.cs (emails a
            // separate reset link per matching account), and ResetPassword.cshtml.cs
            // (disambiguates by which candidate the reset token actually validates against) -
            // all three MUST be kept in sync with this index; do not reintroduce tenant-scoped
            // username uniqueness without them.
            builder.Entity<AppUser>().HasIndex(u => u.NormalizedUserName).IsUnique(false);
            builder.Entity<AppUser>()
                .HasIndex(new[] { nameof(AppUser.TenantId), nameof(AppUser.NormalizedUserName) }, "TenantNormalizedUserNameIndex")
                .IsUnique()
                .HasFilter("[TenantId] IS NOT NULL");
            builder.Entity<AppUser>()
                .HasIndex(new[] { nameof(AppUser.TenantId), nameof(AppUser.NormalizedUserName) }, "SuperAdminNormalizedUserNameIndex")
                .IsUnique()
                .HasFilter("[TenantId] IS NULL");

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
