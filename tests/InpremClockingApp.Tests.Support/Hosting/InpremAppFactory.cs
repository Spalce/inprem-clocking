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
/// <item>the connection string points at the fixture's own database;</item>
/// <item><c>SeedAdmin:*</c> / <c>SeedSuperAdmin:*</c> are blanked, so no account is created from configuration;</item>
/// <item>data-protection keys are in memory, not written to the project's <c>App_Data</c>;</item>
/// <item><see cref="BillingBackgroundService"/> is not started, so no timer races the tests.</item>
/// </list>
/// </summary>
internal class InpremAppFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    private readonly string _connectionString;

    public InpremAppFactory(string connectionString)
    {
        _connectionString = connectionString;
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["SeedAdmin:Email"] = "",
                ["SeedAdmin:Password"] = "",
                ["SeedSuperAdmin:Email"] = "",
                ["SeedSuperAdmin:Password"] = "",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Program.cs captures the connection string in its AddDbContext lambda; replace the
            // options so the test database is used whatever the machine's environment says.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_connectionString));

            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            var billingWorker = services.SingleOrDefault(d => d.ImplementationType == typeof(BillingBackgroundService));
            if (billingWorker != null) services.Remove(billingWorker);
        });
    }
}
