using System.Net;
using System.Text;
using InpremClockingApp.Hosting;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Inprem.Platform.Tests.Integration.HostIsolation;

/// <summary>
/// Step 7.6: the security headers on every kind of response of both sites; the Content-Security-Policy
/// in its three modes (ReportOnly by default); the browsers' policy reports, logged without query strings.
/// </summary>
public class SecurityHeadersTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync(nameof(SecurityHeadersTests));

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private InpremAppFactory Host(string mode = "All", string? csp = null) =>
        SiteHosts.Factory(_db.ConnectionString, mode, extra: csp == null ? null : new Dictionary<string, string?> { ["SecurityHeaders:ContentSecurityPolicy"] = csp });

    private static void AssertFixedHeaders(HttpResponseMessage response, string what)
    {
        foreach (var (name, value) in SecurityHeaders.Fixed)
        {
            if (name == "Referrer-Policy") continue; // checked separately: the console sends a stricter one
            Assert.True(response.Headers.TryGetValues(name, out var values), $"{what}: no {name}.");
            Assert.Equal(value, values!.Single());
        }
        Assert.True(response.Headers.Contains("Referrer-Policy"), $"{what}: no Referrer-Policy.");
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]                 // an approved page
    [InlineData("/BackOffice")]                             // a redirect to sign-in
    [InlineData("/css/site.css")]                           // a static file
    [InlineData("/does-not-exist")]                         // not found
    [InlineData("/api/public/v1/health")]                   // the website API
    [InlineData("/Provider/Login")]                         // the console
    [InlineData("/staff-clockin/1")]                        // a kiosk page (redirect)
    public async Task Every_Kind_Of_Response_Has_The_Headers_And_A_Report_Only_Policy_By_Default(string url)
    {
        await using var app = Host();
        using var response = await app.Client().GetAsync(url);
        AssertFixedHeaders(response, url);
        Assert.Equal(SecurityHeaders.Policy, response.Headers.GetValues("Content-Security-Policy-Report-Only").Single());
        Assert.False(response.Headers.Contains("Content-Security-Policy"), $"{url}: the policy must not be enforced by default.");
    }

    [Fact]
    public async Task The_Console_Keeps_Its_No_Referrer_And_The_App_Gets_Strict_Origin()
    {
        await using var app = Host();
        using var client = app.Client();
        using var console = await client.GetAsync("/Provider/Login");
        Assert.Equal("no-referrer", console.Headers.GetValues("Referrer-Policy").Single());
        using var page = await client.GetAsync("/Identity/Account/Login");
        Assert.Equal("strict-origin-when-cross-origin", page.Headers.GetValues("Referrer-Policy").Single());
    }

    [Theory]
    [InlineData("App", "/Identity/Account/Login")]
    [InlineData("Admin", "/Provider/Login")]
    public async Task Both_Sites_Send_The_Headers(string mode, string url)
    {
        await using var app = Host(mode);
        using var response = await app.Client().GetAsync(url); // the Admin site refuses (404) outside its allowed networks: refusals get the headers too
        AssertFixedHeaders(response, $"{mode} {url}");
        Assert.True(response.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Theory]
    [InlineData("Enforce", "Content-Security-Policy", "Content-Security-Policy-Report-Only")]
    [InlineData("ReportOnly", "Content-Security-Policy-Report-Only", "Content-Security-Policy")]
    public async Task The_Setting_Chooses_Enforce_Or_Report_Only(string mode, string sent, string notSent)
    {
        await using var app = Host(csp: mode);
        using var response = await app.Client().GetAsync("/Identity/Account/Login");
        Assert.Equal(SecurityHeaders.Policy, response.Headers.GetValues(sent).Single());
        Assert.False(response.Headers.Contains(notSent));
    }

    [Fact]
    public async Task Off_Sends_No_Policy_But_Keeps_The_Other_Headers()
    {
        await using var app = Host(csp: "Off");
        using var response = await app.Client().GetAsync("/Identity/Account/Login");
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
        AssertFixedHeaders(response, "Off");
    }

    [Fact]
    public async Task An_Unknown_Mode_Stops_The_App_At_Start_Up()
    {
        await using var app = Host(csp: "Sometimes");
        var error = Assert.ThrowsAny<Exception>(() => app.Client());
        Assert.Contains("ContentSecurityPolicy", error.ToString());
    }

    [Fact]
    public async Task The_Policy_Allows_What_The_Pages_Use_And_Nothing_Wider()
    {
        // Everything else (other sites' scripts, plugins, being framed elsewhere) falls back to 'self' or none.
        var policy = SecurityHeaders.Policy;
        Assert.StartsWith("default-src 'self'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("frame-ancestors 'self'", policy);
        Assert.Contains("base-uri 'self'", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
        Assert.DoesNotContain(" * ", policy + " ");
        Assert.DoesNotContain("https:;", policy + ";");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_Browser_Report_Is_Logged_Without_Query_Strings()
    {
        var logs = new CapturingLoggerProvider();
        await using var app = new InpremAppFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Site:SiteMode"] = "All" },
            configureServices: services => services.AddSingleton<ILoggerProvider>(logs));
        using var client = app.Client();

        const string report = """
            {"csp-report":{"document-uri":"https://app.inprem.test/Account/VerifyEmail?code=SECRET-CODE","violated-directive":"script-src-elem",
            "effective-directive":"script-src-elem","blocked-uri":"https://evil.example/x.js?token=SECRET-TOKEN","original-policy":"default-src 'self'"}}
            """;
        using var response = await client.PostAsync(SecurityHeaders.ReportPath, new StringContent(report, Encoding.UTF8, "application/csp-report"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var line = Assert.Single(logs.Entries, e => e.Message.StartsWith("Content-Security-Policy report", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("script-src-elem blocked https://evil.example/x.js on https://app.inprem.test/Account/VerifyEmail", line.Message);
        Assert.DoesNotContain("SECRET", line.AllText);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"something":"else"}""")]
    [InlineData("")]
    public async Task A_Bad_Report_Is_Ignored_Quietly(string body)
    {
        var logs = new CapturingLoggerProvider();
        await using var app = new InpremAppFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Site:SiteMode"] = "All" },
            configureServices: services => services.AddSingleton<ILoggerProvider>(logs));
        using var response = await app.Client().PostAsync(SecurityHeaders.ReportPath, new StringContent(body, Encoding.UTF8, "application/csp-report"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(logs.Entries, e => e.Message.StartsWith("Content-Security-Policy report", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reports_Are_Logged_At_Most_Thirty_A_Minute()
    {
        var logs = new CapturingLoggerProvider();
        await using var app = new InpremAppFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Site:SiteMode"] = "All" },
            configureServices: services => services.AddSingleton<ILoggerProvider>(logs));
        using var client = app.Client();
        const string report = """{"csp-report":{"document-uri":"https://a.test/p","effective-directive":"img-src","blocked-uri":"https://b.test/i.png"}}""";
        // Twice the limit and one more: even if a minute boundary falls inside the loop, some are not logged.
        var sent = SecurityHeadersMiddleware.ReportsLoggedPerMinute * 2 + 1;
        for (var i = 0; i < sent; i++)
            using (await client.PostAsync(SecurityHeaders.ReportPath, new StringContent(report, Encoding.UTF8, "application/csp-report"))) { }

        var logged = logs.Entries.Count(e => e.Message.StartsWith("Content-Security-Policy report", StringComparison.Ordinal));
        Assert.InRange(logged, SecurityHeadersMiddleware.ReportsLoggedPerMinute, sent - 1);
    }

    [Theory]
    [InlineData("https://a.test/p?code=1#x", "https://a.test/p")]
    [InlineData("inline", "inline")]
    [InlineData(null, null)]
    public void Addresses_Lose_Their_Query_And_Fragment(string? address, string? expected) =>
        Assert.Equal(expected, SecurityHeadersMiddleware.WithoutQuery(address));
}
