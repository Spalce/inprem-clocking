using System.Net;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.HostIsolation;

/// <summary>Phase 1 step 1.1, D17 (P1-7): the Admin site refuses addresses outside AdminSite:AllowedNetworks.</summary>
public class AdminAllowListTests : IAsyncLifetime
{
    private const string TestIpHeader = "X-Test-Remote-Ip";
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync(nameof(AdminAllowListTests));

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>The in-memory server has no client address; this lets a test choose one.</summary>
    private sealed class RemoteIpFromHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[TestIpHeader], out var address)) context.Connection.RemoteIpAddress = address;
                await nextMiddleware();
            });
            next(app);
        };
    }

    private InpremAppFactory Host(string siteMode, params string[] networks)
    {
        var settings = new Dictionary<string, string?> { ["Site:SiteMode"] = siteMode };
        for (var i = 0; i < networks.Length; i++) settings[$"AdminSite:AllowedNetworks:{i}"] = networks[i];
        return new InpremAppFactory(_db.ConnectionString, settings, configureServices: s => s.AddTransient<IStartupFilter, RemoteIpFromHeader>());
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string path, string? ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (ip != null) request.Headers.Add(TestIpHeader, ip);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task AllowList_Admin_Site_Refuses_Outside_Addresses()
    {
        await using var admin = Host("Admin", "203.0.113.0/24", "198.51.100.7");
        using var client = admin.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/Login", "203.0.113.45"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/Login", "198.51.100.7"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/Login", "::ffff:203.0.113.9")); // IPv4 seen through IPv6
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(client, "/Login", "198.51.100.8"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(client, "/Login", "192.0.2.1"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(client, "/Login", null));      // unknown address
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(client, "/health", "192.0.2.1"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(client, "/setup", "192.0.2.1"));
    }

    [Fact]
    public async Task AllowList_On_A_Combined_Site_Covers_Only_The_Console()
    {
        await using var all = Host("All", "203.0.113.0/24");
        using var client = all.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/Identity/Account/Login", "192.0.2.1"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(client, "/Provider/Login", "192.0.2.1"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/Provider/Login", "203.0.113.1"));
    }

    [Fact]
    public async Task AllowList_Empty_Allows_Everyone_And_App_Site_Ignores_It()
    {
        await using (var admin = Host("Admin"))
        {
            Assert.Equal(HttpStatusCode.OK, await GetAsync(admin.CreateClient(), "/Login", "192.0.2.1"));
        }

        await using var app = Host("App", "203.0.113.0/24");
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.CreateClient(), "/Identity/Account/Login", "192.0.2.1"));
    }

    [Fact]
    public async Task AllowList_Invalid_Entry_Stops_Startup()
    {
        await using var admin = Host("Admin", "not-an-address");
        var error = Assert.ThrowsAny<Exception>(() => admin.Server);
        Assert.Contains("AdminSite:AllowedNetworks", error.ToString());
    }
}
