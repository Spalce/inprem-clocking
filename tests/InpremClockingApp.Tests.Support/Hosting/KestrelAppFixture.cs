using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// Same app and data as <see cref="AppFixture"/>, but also listening on a real loopback port so
/// a browser (Playwright) can reach it. <see cref="ServerAddress"/> is the base URL.
/// </summary>
public class KestrelAppFixture : AppFixture
{
    public Uri ServerAddress => ((KestrelAppFactory)Factory).ServerAddress;

    /// <summary>Settings for this fixture's app (a subclass overrides it, e.g. to switch the demo website on).</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    /// <summary>
    /// Browser tests run with the Content-Security-Policy enforced (step 7.6), so a page script or
    /// style the policy would block fails the test that uses it. A subclass may override it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> BrowserDefaults = new Dictionary<string, string?>
    {
        ["SecurityHeaders:ContentSecurityPolicy"] = "Enforce",
    };

    private protected override InpremAppFactory CreateFactory(string connectionString)
    {
        var settings = new Dictionary<string, string?>(BrowserDefaults);
        foreach (var (key, value) in Settings) settings[key] = value;
        return new KestrelAppFactory(connectionString, settings);
    }

    private protected override void StartServer(InpremAppFactory factory) => ((KestrelAppFactory)factory).StartKestrel();

    private sealed class KestrelAppFactory : InpremAppFactory
    {
        private IHost? _kestrelHost;

        public KestrelAppFactory(string connectionString, IReadOnlyDictionary<string, string?> settings) : base(connectionString, settings)
        {
        }

        public Uri ServerAddress { get; private set; } = null!;

        public void StartKestrel()
        {
            // Building the in-memory server triggers CreateHost below, which also starts Kestrel.
            _ = Server;
        }

        // WebApplicationFactory in .NET 8 only builds an in-memory TestServer. Build the host
        // twice: first with Kestrel on a free loopback port for the browser, then again with the
        // TestServer, which is handed back so the factory's own APIs keep working. Each build
        // runs Program.cs (including IdentitySeeder), so the Kestrel host is fully started
        // before the second build begins; otherwise both seed roles at once and collide.
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));
            _kestrelHost = builder.Build();
            _kestrelHost.Start();

            var addresses = _kestrelHost.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            ServerAddress = new Uri(addresses.Addresses.First());

            builder.ConfigureWebHost(web => web.UseTestServer());
            var testHost = builder.Build();
            testHost.Start();
            return testHost;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _kestrelHost?.StopAsync().GetAwaiter().GetResult();
                _kestrelHost?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
