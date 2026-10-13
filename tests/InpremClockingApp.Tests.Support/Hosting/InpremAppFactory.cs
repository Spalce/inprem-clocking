using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Data;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// Hosts the real app (unchanged Program.cs) against a test database. Differences from a
/// normal run, all limited to the test host:
/// <list type="bullet">
/// <item>environment <c>Testing</c>: no user-secrets or per-environment appsettings are loaded;</item>
/// <item>both DbContexts point at the test database;</item>
/// <item><c>SeedAdmin:*</c> / <c>SeedSuperAdmin:*</c> are blanked, so no account is created from configuration;</item>
/// <item>data-protection keys are in memory unless <c>persistKeys</c> is set (then they go to the
/// test database's platform.DataProtectionKeys, as in a real deployment);</item>
/// <item><see cref="BillingBackgroundService"/> is not started, so no timer races the tests;</item>
/// <item>optional extra settings (e.g. <c>Site:SiteMode</c>) override appsettings.json.</item>
/// </list>
/// </summary>
internal class InpremAppFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly bool _persistKeys;
    private readonly Action<IServiceCollection>? _configureServices;

    public InpremAppFactory(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null,
        bool persistKeys = false,
        Action<IServiceCollection>? configureServices = null)
    {
        _connectionString = connectionString;
        _settings = settings ?? new Dictionary<string, string?>();
        _persistKeys = persistKeys;
        _configureServices = configureServices;
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["SeedAdmin:Email"] = "",
                ["SeedAdmin:Password"] = "",
                ["SeedSuperAdmin:Email"] = "",
                ["SeedSuperAdmin:Password"] = "",
            };
            foreach (var (key, value) in _settings) values[key] = value;
            config.AddInMemoryCollection(values);
        });

        builder.ConfigureTestServices(services =>
        {
            // Program.cs captures the connection string when it registers the contexts; replace
            // their options so the test database is used whatever the machine's environment says.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_connectionString));
            services.RemoveAll<DbContextOptions<PlatformDbContext>>();
            services.AddDbContext<PlatformDbContext>(options => PlatformDbContext.Configure(options, _connectionString));

            if (!_persistKeys)
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            }

            var billingWorker = services.SingleOrDefault(d => d.ImplementationType == typeof(BillingBackgroundService));
            if (billingWorker != null) services.Remove(billingWorker);

            _configureServices?.Invoke(services);
        });
    }
}
