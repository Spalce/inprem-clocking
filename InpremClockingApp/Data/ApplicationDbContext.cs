using Inprem.Platform.Domain.Memberships;
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
        public virtual DbSet<Tenant> Tenants { get; set; } = null!;
        public virtual DbSet<Subscription> Subscriptions { get; set; } = null!;
        public virtual DbSet<Invoice> Invoices { get; set; } = null!;

        /// <summary>
        /// Read-only view of platform.TenantMemberships (owned by PlatformDbContext's migrations),
        /// used by the AppUser query filter. Never written through this context.
        /// </summary>
        public virtual DbSet<TenantMembership> MembershipsView { get; set; } = null!;

        // C2-1 (multi-tenancy Phase 0 step 0.5): tenant rules on every save - see
        // TenantSaveChangesInterceptor. Registered here rather than at AddDbContext so that every
        // way of building this context (the app, tests, tools) gets it.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.AddInterceptors(TenantSaveChangesInterceptor.Instance);
            base.OnConfiguring(optionsBuilder);
        }

        /// <summary>The organization this context works for (null: none), for the save interceptor.</summary>
        internal int? CurrentTenantId => _currentTenant.TenantId;

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

            // C2-1 (multi-tenancy Phase 0 step 0.4, migration M3): an email belongs to one account
            // platform-wide (decision D2: one account per person, any number of memberships in
            // platform.TenantMemberships). This makes the per-tenant username indexes below - and the
            // pick-by-password login they once required - unnecessary; they stay until the contract
            // migration so that rolling back step 0.4 remains possible. Reconfigures Identity's own
            // "EmailIndex" (previously non-unique).
            builder.Entity<AppUser>().HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
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

            // (The old tables only: BillingService was removed in multi-tenancy Phase 2 step 2.9 and
            // the tables are dropped in Phase 7, M6. Kept mapped so the app's schema does not change.)
            // Backed BillingService.GenerateInvoiceAsync's idempotency check ("does a non-void
            // invoice already exist for this period") with a real DB constraint, the same
            // defense-in-depth every other one-row-per-tenant invariant in this schema has
            // (Subscription). Filtered (not a plain unique index) because voiding a
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
            builder.Entity<Subscription>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Invoice>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);

            // A tenant-scoped user can only ever see their own Tenant row (defensive - nothing
            // reads this yet, but a future "Organization Settings" page would). SuperAdmin
            // tenant-management (Phase 4) bypasses this deliberately via IgnoreQueryFilters().
            builder.Entity<Tenant>().HasQueryFilter(t => t.Id == _currentTenant.TenantId);

            // C2-1 (multi-tenancy Phase 0 step 0.5): the AppUser filter fails closed. Without an
            // organization (anonymous, startup, platform operators) no account is visible through
            // queries; with one, the visible accounts are its members - an Active membership in
            // platform.TenantMemberships, or an account of this organization that has no membership
            // row at all yet (created by an older path; it gets one at first sign-in). Sign-in and
            // lookups by id, user name or email go through AppUserStore, which deliberately looks
            // past this filter: that is the one audited place for unfiltered account lookups.
            builder.Entity<AppUser>().HasQueryFilter(u =>
                _currentTenant.TenantId != null &&
                (MembershipsView.Any(m => m.UserId == u.Id && m.TenantId == _currentTenant.TenantId && m.Status == MembershipStatus.Active)
                 || (u.TenantId == _currentTenant.TenantId && !MembershipsView.Any(m => m.UserId == u.Id && m.TenantId == _currentTenant.TenantId))));

            builder.Entity<TenantMembership>(membership =>
            {
                membership.ToTable("TenantMemberships", "platform", t => t.ExcludeFromMigrations());
                membership.HasKey(m => m.Id);
                membership.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
                membership.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
                membership.Ignore(m => m.IsActive);
            });

            // C2-1 (multi-tenancy Phase 0 step 0.5, migration M4): a session can only point at a
            // person of its own organization. The clocking foreign keys become composite -
            // (TenantId, StafId) -> Staffs(TenantId, StaffId) and (TenantId, VoluntId) ->
            // Volunteers(TenantId, VolunteerId) - backed by alternate keys on the people tables.
            // Configured here only; the model classes are unchanged. Delete behaviour stays Cascade.
            builder.Entity<Staff>().HasAlternateKey(s => new { s.TenantId, s.StaffId });
            builder.Entity<Volunteer>().HasAlternateKey(v => new { v.TenantId, v.VolunteerId });
            builder.Entity<ClockingStaff>()
                .HasOne(c => c.Staff).WithMany(s => s.ClockingsStaff)
                .HasForeignKey(c => new { c.TenantId, c.StafId })
                .HasPrincipalKey(s => new { s.TenantId, s.StaffId })
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Clocking>()
                .HasOne(c => c.Volunteer).WithMany(v => v.Clockings)
                .HasForeignKey(c => new { c.TenantId, c.VoluntId })
                .HasPrincipalKey(v => new { v.TenantId, v.VolunteerId })
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
