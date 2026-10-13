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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Options shared by the app and the design-time factory.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));
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
