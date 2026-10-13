using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Data;
using InpremClockingApp.Services;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Tests.Support.Database;

/// <summary>
/// A fresh test database with every migration of both contexts applied (the approved app's
/// ApplicationDbContext and the platform's PlatformDbContext), dropped on dispose.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync(string purpose)
    {
        var database = new TestDatabase(await TestSqlServer.NewDatabaseConnectionStringAsync(purpose).ConfigureAwait(false));
        await using (var app = database.CreateDbContext())
        {
            await app.Database.MigrateAsync().ConfigureAwait(false);
        }
        await using (var platform = database.CreatePlatformDbContext())
        {
            await platform.Database.MigrateAsync().ConfigureAwait(false);
        }
        return database;
    }

    /// <summary>The approved app's context with no tenant resolved (use IgnoreQueryFilters to see tenant rows).</summary>
    public ApplicationDbContext CreateDbContext(int? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options, new FixedTenant(tenantId));
    }

    public PlatformDbContext CreatePlatformDbContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PlatformDbContext.Configure(options, ConnectionString);
        return new PlatformDbContext(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync().ConfigureAwait(false);
    }

    private sealed class FixedTenant : ICurrentTenantService
    {
        public FixedTenant(int? tenantId) => TenantId = tenantId;
        public int? TenantId { get; }
    }
}
