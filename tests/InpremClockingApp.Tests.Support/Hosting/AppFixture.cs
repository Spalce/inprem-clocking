using InpremClockingApp.Data;
using InpremClockingApp.Services;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// One running app over one freshly migrated and seeded database. Use as an xUnit class or
/// collection fixture; every fixture instance gets its own database, dropped on dispose.
/// Order matters: Program.cs runs IdentitySeeder at startup, so the schema must exist before
/// the host starts, and the seed data needs the roles that startup creates.
/// </summary>
public class AppFixture : IAsyncLifetime
{
    private InpremAppFactory? _factory;

    public string ConnectionString { get; private set; } = "";
    public SeedCatalog Data { get; private set; } = null!;

    /// <summary>The app's service provider (create a scope before resolving scoped services).</summary>
    public IServiceProvider Services => Factory.Services;

    private protected InpremAppFactory Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public async Task InitializeAsync()
    {
        ConnectionString = await TestSqlServer.NewDatabaseConnectionStringAsync(GetType().Name).ConfigureAwait(false);

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync().ConfigureAwait(false);
        }

        _factory = CreateFactory(ConnectionString);
        StartServer(_factory);

        Data = await TestDataSeeder.SeedAsync(_factory.Services).ConfigureAwait(false);
    }

    private protected virtual InpremAppFactory CreateFactory(string connectionString) => new(connectionString);

    private protected virtual void StartServer(InpremAppFactory factory) => _ = factory.Server;

    /// <summary>
    /// A client for the in-memory server: https://localhost, cookies kept, redirects NOT followed
    /// (tests assert on them).
    /// </summary>
    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>
    /// A context on the test database with no tenant resolved, for arranging and checking rows.
    /// Tenant-scoped sets return nothing unless the query uses <c>IgnoreQueryFilters()</c>.
    /// </summary>
    public ApplicationDbContext CreateDbContext(int? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options, new FixedTenant(tenantId));
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(ConnectionString))
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    private sealed class FixedTenant : ICurrentTenantService
    {
        public FixedTenant(int? tenantId) => TenantId = tenantId;
        public int? TenantId { get; }
    }
}
