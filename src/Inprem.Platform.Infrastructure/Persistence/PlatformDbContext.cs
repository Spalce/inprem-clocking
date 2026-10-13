using Inprem.Platform.Domain.Memberships;
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
            membership.Ignore(m => m.IsActive);
            // One membership per person per organization; Revoked rows stay as history.
            membership.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();
            membership.HasIndex(m => m.TenantId);
        });

        modelBuilder.Entity<TenantRow>(tenant =>
        {
            tenant.ToTable("Tenants", "dbo", t => t.ExcludeFromMigrations());
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Name).HasMaxLength(200);
        });
    }

    /// <summary>Options shared by the app and the design-time factory.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));
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
