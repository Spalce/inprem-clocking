using Microsoft.Extensions.DependencyInjection;
using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Data;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.TestData;
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
    private TestDatabase? _database;

    public string ConnectionString => Database.ConnectionString;
    public SeedCatalog Data { get; private set; } = null!;

    /// <summary>Emails the app sent (nothing is really sent in tests).</summary>
    public CapturingEmailSender Emails => Factory.Emails;

    /// <summary>The pretend PayPal (nothing reaches PayPal in tests).</summary>
    public FakePaymentProvider PayPal => Factory.PayPal;

    public FakeInvoiceProvider Invoicing => Factory.Invoicing;

    /// <summary>Runs the billing-link emails the app queued (as the job server would).</summary>
    public Task RunBillingAccessJobsAsync() =>
        ((InlineBillingAccessQueue)Services.GetRequiredService<Inprem.Platform.Application.PublicApi.IBillingAccessQueue>()).RunAllAsync();

    /// <summary>The app's service provider (create a scope before resolving scoped services).</summary>
    public IServiceProvider Services => Factory.Services;

    private TestDatabase Database => _database ?? throw new InvalidOperationException("Fixture not initialized.");

    private protected InpremAppFactory Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(GetType().Name).ConfigureAwait(false);

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
    public ApplicationDbContext CreateDbContext(int? tenantId = null) => Database.CreateDbContext(tenantId);

    public PlatformDbContext CreatePlatformDbContext() => Database.CreatePlatformDbContext();

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }

        if (_database != null)
        {
            await _database.DisposeAsync().ConfigureAwait(false);
        }
    }
}
