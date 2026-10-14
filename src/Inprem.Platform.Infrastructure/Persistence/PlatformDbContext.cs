using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Organizations;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Domain.Support;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inprem.Platform.Infrastructure.Persistence;

/// <summary>
/// The platform's own tables, in schema <c>platform</c> on the same database as the approved app
/// (decision D3). <c>ApplicationDbContext</c> keeps the existing tables. Migrations live in this
/// project (Persistence/Migrations) and are never applied at startup (D12): they are deployed as
/// reviewed idempotent scripts (scripts/migrations/).
/// </summary>
public class PlatformDbContext : DbContext, IDataProtectionKeyContext
{
    public const string Schema = "platform";

    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options)
    {
    }

    /// <summary>ASP.NET Core data-protection keys, shared by every instance of a site.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    public DbSet<TenantMembership> TenantMemberships { get; set; } = null!;

    /// <summary>Read-only view of the approved app's dbo.Tenants (owned by ApplicationDbContext's migrations).</summary>
    public DbSet<TenantRow> Tenants { get; set; } = null!;

    /// <summary>Read-only view of the account columns the platform reads from dbo.AspNetUsers.</summary>
    public DbSet<AccountRow> Accounts { get; set; } = null!;

    public DbSet<ProviderUser> ProviderUsers { get; set; } = null!;
    public DbSet<Invitation> ProviderInvitations { get; set; } = null!;
    public DbSet<RecoveryRecord> RecoveryRecords { get; set; } = null!;

    /// <summary>Append-only (refused here on save, and by a trigger in the database).</summary>
    public DbSet<AuditEntry> AuditEntries { get; set; } = null!;
    public DbSet<UsedCode> UsedCodes { get; set; } = null!;
    public DbSet<SecurityAttempt> SecurityAttempts { get; set; } = null!;

    public DbSet<Plan> Plans { get; set; } = null!;
    public DbSet<PlanPrice> PlanPrices { get; set; } = null!;

    public DbSet<TenantProfile> TenantProfiles { get; set; } = null!;
    public DbSet<TenantNote> TenantNotes { get; set; } = null!;
    public DbSet<Subscription> Subscriptions { get; set; } = null!;
    public DbSet<SupportSession> SupportSessions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TenantMembership>(membership =>
        {
            membership.ToTable("TenantMemberships");
            membership.HasKey(m => m.Id);
            membership.Property(m => m.Id).ValueGeneratedNever();
            // Same length as AspNetUsers.Id; the foreign keys to dbo tables are added in the migration's SQL.
            membership.Property(m => m.UserId).HasMaxLength(450).IsRequired();
            membership.Property(m => m.InvitedByUserId).HasMaxLength(450);
            membership.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
            membership.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            membership.Property(m => m.CreatedUtc).HasColumnType("datetime2");
            membership.Property(m => m.LastSignInUtc).HasColumnType("datetime2");
            membership.Ignore(m => m.IsActive);
            // One membership per person per organization; Revoked rows stay as history.
            membership.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();
            membership.HasIndex(m => m.TenantId);
        });

        modelBuilder.Entity<ProviderUser>(user =>
        {
            user.ToTable("ProviderUsers");
            user.HasKey(u => u.UserId);
            user.Property(u => u.UserId).HasMaxLength(450);
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            user.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
            user.Property(u => u.CreatedUtc).HasColumnType("datetime2");
            user.Ignore(u => u.IsActiveOwner);
        });

        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("ProviderInvitations");
            invitation.HasKey(i => i.Id);
            invitation.Property(i => i.Id).ValueGeneratedNever();
            invitation.Property(i => i.Email).HasMaxLength(256);
            invitation.Property(i => i.UserId).HasMaxLength(450);
            invitation.Property(i => i.Purpose).HasConversion<string>().HasMaxLength(30);
            invitation.Property(i => i.TokenHash).HasMaxLength(64).IsUnicode(false);
            invitation.Property(i => i.CreatedBy).HasMaxLength(300);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.HasIndex(i => i.UserId);
        });

        modelBuilder.Entity<RecoveryRecord>(recovery =>
        {
            recovery.ToTable("RecoveryRecords");
            recovery.HasKey(r => r.Id);
            recovery.Property(r => r.Id).ValueGeneratedNever();
            recovery.Property(r => r.Level).HasConversion<string>().HasMaxLength(30);
            recovery.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            recovery.Property(r => r.TargetUserId).HasMaxLength(450);
            recovery.Property(r => r.TargetEmail).HasMaxLength(256);
            recovery.Property(r => r.NewEmail).HasMaxLength(256);
            recovery.Property(r => r.Operator).HasMaxLength(300);
            recovery.Property(r => r.Reason).HasMaxLength(1000);
            recovery.Property(r => r.FirstApproverEmail).HasMaxLength(256);
            recovery.Property(r => r.SecondApproverEmail).HasMaxLength(256);
            recovery.Property(r => r.FirstApprovalTokenHash).HasMaxLength(64).IsUnicode(false);
            recovery.Property(r => r.SecondApprovalTokenHash).HasMaxLength(64).IsUnicode(false);
            recovery.HasIndex(r => r.FirstApprovalTokenHash).IsUnique().HasFilter("[FirstApprovalTokenHash] IS NOT NULL");
            recovery.HasIndex(r => r.SecondApprovalTokenHash).IsUnique().HasFilter("[SecondApprovalTokenHash] IS NOT NULL");
            recovery.HasIndex(r => new { r.TargetUserId, r.CreatedUtc });
        });

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("AuditEntries");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Id).UseIdentityColumn();
            audit.Property(a => a.Actor).HasMaxLength(300);
            audit.Property(a => a.Action).HasMaxLength(100);
            audit.Property(a => a.Target).HasMaxLength(450);
            audit.Property(a => a.Details).HasMaxLength(2000);
            audit.HasIndex(a => a.OccurredUtc);
        });

        modelBuilder.Entity<UsedCode>(code =>
        {
            code.ToTable("UsedCodes");
            code.HasKey(c => c.CodeHash);
            code.Property(c => c.CodeHash).HasMaxLength(64).IsUnicode(false);
            code.Property(c => c.Purpose).HasMaxLength(30);
        });

        modelBuilder.Entity<SecurityAttempt>(attempt =>
        {
            attempt.ToTable("SecurityAttempts");
            attempt.HasKey(a => a.Id);
            attempt.Property(a => a.Kind).HasMaxLength(30);
            attempt.Property(a => a.Source).HasMaxLength(300);
            attempt.HasIndex(a => new { a.Kind, a.OccurredUtc });
        });

        modelBuilder.Entity<Plan>(plan =>
        {
            plan.ToTable("Plans");
            plan.HasKey(p => p.Id);
            plan.Property(p => p.Id).ValueGeneratedNever();
            plan.Property(p => p.Code).HasMaxLength(50);
            plan.Property(p => p.Name).HasMaxLength(100);
            plan.Property(p => p.Description).HasMaxLength(1000);
            plan.Property(p => p.Features).HasMaxLength(2000);
            plan.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            plan.Ignore(p => p.IsOffered);
            plan.HasIndex(p => p.Code).IsUnique();
            plan.HasMany(p => p.Prices).WithOne().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);
            plan.Navigation(p => p.Prices).HasField("_prices").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PlanPrice>(price =>
        {
            price.ToTable("PlanPrices");
            price.HasKey(p => p.Id);
            price.Property(p => p.Id).ValueGeneratedNever();
            price.Property(p => p.Cycle).HasConversion<string>().HasMaxLength(20);
            price.Property(p => p.Amount).HasPrecision(10, 2);
            price.Property(p => p.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            price.Property(p => p.PayPalPlanId).HasMaxLength(50);
            price.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            price.HasIndex(p => p.PlanId);
        });

        modelBuilder.Entity<TenantProfile>(profile =>
        {
            profile.ToTable("TenantProfiles");
            profile.HasKey(p => p.TenantId);
            profile.Property(p => p.TenantId).ValueGeneratedNever();
            profile.Property(p => p.Slug).HasMaxLength(50).IsUnicode(false);
            profile.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            profile.Property(p => p.StatusReason).HasMaxLength(500);
            profile.HasIndex(p => p.Slug).IsUnique();
        });

        modelBuilder.Entity<TenantNote>(note =>
        {
            note.ToTable("TenantNotes");
            note.HasKey(n => n.Id);
            note.Property(n => n.Id).ValueGeneratedNever();
            note.Property(n => n.Text).HasMaxLength(2000);
            note.Property(n => n.Author).HasMaxLength(300);
            note.HasIndex(n => new { n.TenantId, n.CreatedUtc });
        });

        modelBuilder.Entity<Subscription>(subscription =>
        {
            subscription.ToTable("Subscriptions");
            subscription.HasKey(s => s.Id);
            subscription.Property(s => s.Id).ValueGeneratedNever();
            subscription.Property(s => s.Mode).HasConversion<string>().HasMaxLength(20);
            subscription.Property(s => s.Status).HasConversion<string>().HasMaxLength(30);
            subscription.HasIndex(s => s.TenantId).IsUnique();
            subscription.HasOne<Plan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
            subscription.HasOne<PlanPrice>().WithMany().HasForeignKey(s => s.PlanPriceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SupportSession>(session =>
        {
            session.ToTable("SupportSessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.ProviderUserId).HasMaxLength(450);
            session.Property(s => s.ProviderEmail).HasMaxLength(256);
            session.Property(s => s.Reason).HasMaxLength(500);
            session.Property(s => s.EndedBy).HasMaxLength(300);
            session.Property(s => s.HandoffTokenHash).HasMaxLength(64).IsUnicode(false);
            session.HasIndex(s => s.HandoffTokenHash).IsUnique();
            session.HasIndex(s => new { s.TenantId, s.StartedUtc });
        });

        modelBuilder.Entity<AccountRow>(account =>
        {
            account.ToTable("AspNetUsers", "dbo", t => t.ExcludeFromMigrations());
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).HasMaxLength(450);
            account.Property(a => a.Email).HasMaxLength(256);
        });

        modelBuilder.Entity<TenantRow>(tenant =>
        {
            tenant.ToTable("Tenants", "dbo", t => t.ExcludeFromMigrations());
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Name).HasMaxLength(200);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefuseAuditChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefuseAuditChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>The audit log is append-only: an entry is never changed or removed.</summary>
    private void RefuseAuditChanges()
    {
        if (ChangeTracker.Entries<AuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit entries are append-only; they cannot be changed or deleted.");
        }
    }

    /// <summary>Options shared by the app and the design-time factory.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));
}

/// <summary>The columns of dbo.AspNetUsers the platform reads (never written through this context).</summary>
public sealed class AccountRow
{
    public string Id { get; set; } = "";
    public string? Email { get; set; }
    public bool TwoFactorEnabled { get; set; }
}

/// <summary>A one-time code (bootstrap or break-glass) that has been used; only its hash is kept.</summary>
public sealed class UsedCode
{
    public string CodeHash { get; set; } = "";
    public string Purpose { get; set; } = "";
    public DateTime UsedUtc { get; set; }
}

/// <summary>One bootstrap or break-glass attempt, for rate limits and locks.</summary>
public sealed class SecurityAttempt
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public bool Succeeded { get; set; }

    /// <summary>Who or where from: operator name or client address. Never the code.</summary>
    public string Source { get; set; } = "";
    public DateTime OccurredUtc { get; set; }
}

/// <summary>The columns of dbo.Tenants the platform reads.</summary>
public sealed class TenantRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Lets <c>dotnet ef</c> create migrations for this context without starting the web app. The
/// connection string is only used by commands that touch a database (never needed for
/// <c>migrations add</c> or <c>migrations script</c>); set <c>INPREM_PLATFORM_DESIGN_DB</c> to
/// point those at a local database.
/// </summary>
public sealed class PlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("INPREM_PLATFORM_DESIGN_DB")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=InpremPlatformDesignTime;Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PlatformDbContext.Configure(options, connectionString);
        return new PlatformDbContext(options.Options);
    }
}
