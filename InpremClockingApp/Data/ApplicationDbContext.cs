using InpremClockingApp.Models;
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

            // Multi-tenancy Phase 2 (see multi-tenancy.md, decision #3): the actual isolation
            // mechanism. Applied automatically to every query against these DbSets, everywhere
            // in the app - a future `_db.Staffs.Where(...)` call cannot accidentally leak
            // another tenant's rows, because this filter applies before that Where() runs.
            builder.Entity<Staff>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Volunteer>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<ClockingStaff>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Clocking>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
            builder.Entity<Setting>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);

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
