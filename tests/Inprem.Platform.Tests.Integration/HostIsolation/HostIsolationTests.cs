using System.Net;
using System.Security.Cryptography;
using InpremClockingApp.Hosting;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inprem.Platform.Tests.Integration.HostIsolation;

/// <summary>
/// Host-isolation suite (TESTING.md; Phase 0 step 0.2). With SiteMode=App no provider route
/// exists; with SiteMode=Admin no back-office, kiosk, API or webhook route exists; with hosts
/// configured each site answers only on its own host; cookies are host-only and per site;
/// data-protection keys are shared through the database but separate per site.
/// </summary>
public class HostIsolationTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync(nameof(HostIsolationTests));

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Host_App_Mode_Serves_App_Routes_Only()
    {
        await using var app = SiteHosts.Factory(_db.ConnectionString, "App");
        using var client = app.Client();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app-probe")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Identity/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/provider-probe")).StatusCode);
        var endpoints = app.Endpoints();
        Assert.Contains(endpoints, e => e.Pattern == "BackOffice");
        Assert.Contains(endpoints, e => e.Pattern.StartsWith("api/controls/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(endpoints, e => e.Area == SiteMap.ProviderArea);
    }

    [Fact]
    public async Task Host_Admin_Mode_Serves_No_App_Route()
    {
        List<string> appUrls;
        await using (var app = SiteHosts.Factory(_db.ConnectionString, "App"))
        {
            appUrls = app.Endpoints().Where(e => e.Area != SiteMap.ProviderArea)
                .Select(e => SiteHosts.SampleUrl(e.Pattern)).OfType<string>().Distinct().ToList();
        }
        Assert.True(appUrls.Count > 40, $"Only {appUrls.Count} App routes found; the enumeration looks broken.");

        await using var admin = SiteHosts.Factory(_db.ConnectionString, "Admin");
        using var client = admin.Client();

        // On the Admin site the Provider Console is the whole site (/, /Index, /Login, /setup ...):
        // an App URL that is also a console route reaches the console, never the App page.
        var consoleUrls = admin.Endpoints().Where(e => e.Area == SiteMap.ProviderArea)
            .Select(e => SiteHosts.SampleUrl(e.Pattern)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        appUrls = appUrls.Where(u => !consoleUrls.Contains(u)).ToList();

        // Every back-office page, kiosk page, Identity page and API route of the App site is gone.
        foreach (var url in appUrls)
        {
            Assert.True((await client.GetAsync(url)).StatusCode == HttpStatusCode.NotFound, $"GET {url} is served on the Admin site.");
            Assert.True((await client.PostAsync(url, new StringContent(""))).StatusCode == HttpStatusCode.NotFound, $"POST {url} is served on the Admin site.");
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/provider-probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("/")).StatusCode); // console home: sign in first
        // Everything else the Admin site serves is the console: a Provider page, or a console endpoint
        // outside the area (jobs dashboard, health) marked as such.
        var endpoints = admin.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().ToList();
        Assert.All(endpoints, e => Assert.True(
            string.Equals(e.RoutePattern.RequiredValues.GetValueOrDefault("area") as string, SiteMap.ProviderArea, StringComparison.Ordinal)
            || e.Metadata.GetMetadata<ProviderEndpointMetadata>() != null,
            $"{e.RoutePattern.RawText} is served on the Admin site but is not part of the console."));
        Assert.Contains(endpoints, e => e.RoutePattern.RawText!.StartsWith("/jobs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_All_Mode_Serves_Both()
    {
        await using var all = SiteHosts.Factory(_db.ConnectionString, "All");
        using var client = all.Client();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Identity/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/provider-probe")).StatusCode);
    }

    [Fact]
    public async Task Host_App_Mode_With_Host_Answers_Only_On_App_Host()
    {
        await using var app = SiteHosts.Factory(_db.ConnectionString, "App", appHost: "app.inprem.test", adminHost: "admin.inprem.test");

        using var onAppHost = app.Client("app.inprem.test");
        using var onAdminHost = app.Client("admin.inprem.test");
        using var onOtherHost = app.Client("elsewhere.test");

        Assert.Equal(HttpStatusCode.OK, (await onAppHost.GetAsync("/Identity/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await onAppHost.GetAsync("/app-probe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await onAdminHost.GetAsync("/Identity/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await onAdminHost.GetAsync("/app-probe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await onOtherHost.GetAsync("/BackOffice")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await onAdminHost.GetAsync("/provider-probe")).StatusCode);
    }

    [Fact]
    public async Task Host_All_Mode_With_Hosts_Keeps_Each_Site_On_Its_Host()
    {
        await using var all = SiteHosts.Factory(_db.ConnectionString, "All", appHost: "app.inprem.test", adminHost: "admin.inprem.test");

        using var onAppHost = all.Client("app.inprem.test");
        using var onAdminHost = all.Client("admin.inprem.test");

        Assert.Equal(HttpStatusCode.OK, (await onAppHost.GetAsync("/Identity/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await onAdminHost.GetAsync("/Identity/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await onAdminHost.GetAsync("/provider-probe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await onAppHost.GetAsync("/provider-probe")).StatusCode);
    }

    [Fact]
    public async Task Host_App_Cookie_Is_Host_Only_Secure_HttpOnly_Lax()
    {
        await using var app = SiteHosts.Factory(_db.ConnectionString, "App");
        var email = $"cookie-{Guid.NewGuid():N}@inprem.test";
        var password = $"C{Guid.NewGuid():N}!a";
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.CreateAsync(new AppUser { UserName = email, Email = email, EmailConfirmed = true }, password)).Succeeded);
        }
        using var client = app.Client();

        using var login = await SignIn.PostLoginAsync(client, email, password);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(SiteHosting.AppCookieName + "=", StringComparison.Ordinal));
        var attributes = cookie.ToLowerInvariant();
        Assert.Contains("secure", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.DoesNotContain("domain=", attributes);
        Assert.DoesNotContain(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("App", SiteHosting.AppCookieName, SameSiteMode.Lax)]
    [InlineData("All", SiteHosting.AppCookieName, SameSiteMode.Lax)]
    [InlineData("Admin", SiteHosting.AdminCookieName, SameSiteMode.Strict)]
    public async Task Host_Cookie_Settings_Per_Site(string mode, string expectedName, SameSiteMode expectedSameSite)
    {
        await using var factory = SiteHosts.Factory(_db.ConnectionString, mode);

        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme).Cookie;

        Assert.Equal(expectedName, cookie.Name);
        Assert.Null(cookie.Domain);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookie.SecurePolicy);
        Assert.Equal(expectedSameSite, cookie.SameSite);
    }

    [Fact]
    public async Task Host_Data_Protection_Keys_Are_Shared_Through_The_Database_But_Separate_Per_Site()
    {
        string protectedByApp;
        await using (var app1 = SiteHosts.Factory(_db.ConnectionString, "App", persistKeys: true))
        {
            protectedByApp = app1.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("probe").Protect("secret");
        }

        await using (var platform = _db.CreatePlatformDbContext())
        {
            Assert.True(await platform.DataProtectionKeys.AnyAsync(), "No key was written to platform.DataProtectionKeys.");
        }

        // A second App instance (another server, or after a redeploy) reads it...
        await using (var app2 = SiteHosts.Factory(_db.ConnectionString, "App", persistKeys: true))
        {
            Assert.Equal("secret", app2.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("probe").Unprotect(protectedByApp));
        }

        // ...the Admin site, on the same database, cannot.
        await using var admin = SiteHosts.Factory(_db.ConnectionString, "Admin", persistKeys: true);
        var adminProtector = admin.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("probe");
        Assert.ThrowsAny<CryptographicException>(() => adminProtector.Unprotect(protectedByApp));
    }

    [Theory]
    [InlineData(null, null, null, "Site:SiteMode is required")]
    [InlineData("App", "https://app.inprem.test", null, "must be a host name")]
    [InlineData("App", "app.inprem.test/path", null, "must be a host name")]
    [InlineData("All", "same.inprem.test", "same.inprem.test", "must be different hosts")]
    [InlineData("App", null, null, "must be an origin")]
    public async Task Host_Invalid_Site_Settings_Stop_Startup(string? mode, string? appHost, string? adminHost, string expectedMessage)
    {
        var extra = expectedMessage == "must be an origin"
            ? new Dictionary<string, string?> { ["Site:PublicWebsiteOrigins:0"] = "https://inpremclockin.org/pricing" }
            : null;
        await using var factory = SiteHosts.Factory(_db.ConnectionString, mode, appHost, adminHost, extra: extra);

        var error = Record.Exception(() => factory.Server);

        Assert.NotNull(error);
        Assert.Contains(expectedMessage, Messages(error));
    }

    [Fact]
    public async Task Host_Valid_Site_Settings_With_Port_And_Origins_Start()
    {
        await using var factory = SiteHosts.Factory(_db.ConnectionString, "All", "localhost:5001", "localhost:5002",
            extra: new Dictionary<string, string?>
            {
                ["Site:PublicWebsiteOrigins:0"] = "https://inpremclockin.org",
                ["Site:PublicWebsiteOrigins:1"] = "http://localhost:8080",
            });

        Assert.NotNull(factory.Server);
    }

    private static string Messages(Exception error)
    {
        var all = new List<string>();
        for (var e = error; e != null; e = e.InnerException)
        {
            all.Add(e.Message);
            if (e is AggregateException aggregate) all.AddRange(aggregate.InnerExceptions.Select(i => i.Message));
        }
        return string.Join(" | ", all);
    }
}
