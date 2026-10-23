using System.Net;
using InpremClockingApp.Hosting;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inprem.Platform.Tests.Integration.HostIsolation;

/// <summary>
/// Domain cutover step C.1: the per-site settings templates (scripts/deploy/templates) start the app
/// as the site they are for (every setting passes the startup checks), and hold no secrets: those
/// stay in each host's own configuration (docs/multitenancy/runbooks/site-configuration.md).
/// </summary>
public class SiteTemplateTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(nameof(SiteTemplateTests));
        await Jobs.HangfireSchema.InstallAsync(_db.ConnectionString); // the App templates run the job server
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static string Template(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."), "scripts", "deploy", "templates", name);
    }

    private static Dictionary<string, string?> Settings(string name) =>
        new ConfigurationBuilder().AddJsonFile(Template(name)).Build().AsEnumerable()
            .Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value);

    public static TheoryData<string> Templates() => new() { "app-site.production.json", "admin-site.production.json", "test-site.single-host.json" };

    [Theory]
    [MemberData(nameof(Templates))]
    public void A_Template_Holds_No_Secrets(string name)
    {
        var secretKeys = new[] { "Password", "Secret", "AuthToken", "ApiKeySid", "ApiKeySecret", "ClientId", "AccountSid", "MessagingServiceSid", "VerifyServiceSid", "WebhookId", "CodeHash", "ApiKeyHashes", "UserName", "From", "SmtpHost", "Mailbox", "RecoveryApprovers" };
        foreach (var (key, value) in Settings(name))
        {
            Assert.False(key.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase), $"{name}: {key} belongs in the host's configuration.");
            var last = key.Split(':').Where(p => !int.TryParse(p, out _)).Last();
            Assert.False(secretKeys.Contains(last, StringComparer.OrdinalIgnoreCase), $"{name}: {key} belongs in the host's configuration.");
            Assert.DoesNotContain("@", value);
        }
    }

    [Theory]
    [InlineData("app-site.production.json", SiteMode.App)]
    [InlineData("admin-site.production.json", SiteMode.Admin)]
    [InlineData("test-site.single-host.json", SiteMode.All)]
    public async Task A_Template_Starts_The_Site_It_Is_For(string name, SiteMode mode)
    {
        await using var app = new InpremAppFactory(_db.ConnectionString, Settings(name));
        _ = app.Server; // startup checks every section (ValidateOnStart)
        Assert.Equal(mode, app.Services.GetRequiredService<IOptions<SiteOptions>>().Value.SiteMode);

        var host = mode == SiteMode.Admin ? "admin.inpremclockin.org" : "app.inpremclockin.org";
        using var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{host}"), AllowAutoRedirect = false });
        var appLogin = await client.GetAsync("/Identity/Account/Login");
        Assert.Equal(mode == SiteMode.Admin ? HttpStatusCode.NotFound : HttpStatusCode.OK, appLogin.StatusCode);
    }

    [Fact]
    public void The_Two_Production_Sites_Agree_On_Their_Addresses()
    {
        var app = Settings("app-site.production.json");
        var admin = Settings("admin-site.production.json");
        foreach (var key in new[] { "Site:AdminHost", "Links:AppBaseUrl", "Links:AdminBaseUrl", "Links:WebsiteBaseUrl", "Twilio:WebhookBaseUrl" })
            Assert.Equal(app[key], admin[key]);
        Assert.Equal((true, false), (bool.Parse(app["Jobs:RunServer"]!), bool.Parse(admin["Jobs:RunServer"]!))); // jobs on the App site only (D6)
        Assert.Equal("Sandbox", app["PayPal:Mode"]); // live payments are not switched on by any step in this pack
    }
}
