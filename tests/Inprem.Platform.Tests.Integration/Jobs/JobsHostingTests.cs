using System.Collections.Concurrent;
using System.Net;
using Hangfire;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Hosting;
using InpremClockingApp.Services;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Jobs;

/// <summary>A job the tests enqueue to prove a server really processes jobs.</summary>
public static class JobProbe
{
    public static readonly ConcurrentDictionary<string, bool> Ran = new();

    public static void Run(string id) => Ran[id] = true;
}

/// <summary>
/// Phase 0 step 0.8 (D6): Hangfire storage on every site; the job server only on the App site
/// with Jobs:RunServer; the Admin site never starts one and shows a read-only dashboard to
/// Provider Owners only. BillingBackgroundService is gone.
/// </summary>
public class JobsHostingTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(nameof(JobsHostingTests));

        // The Hangfire tables come from the reviewed script, as in a deployment (the app never creates them).
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "scripts", "migrations", "hangfire-up.idempotent.sql"));
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(script, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private InpremAppFactory Host(string siteMode, bool? runServer = null)
    {
        var settings = new Dictionary<string, string?> { ["Site:SiteMode"] = siteMode };
        if (runServer != null) settings["Jobs:RunServer"] = runServer.Value ? "true" : "false";
        return new InpremAppFactory(_db.ConnectionString, settings);
    }

    private static JobServerHost ServerHost(InpremAppFactory app) =>
        app.Services.GetServices<IHostedService>().OfType<JobServerHost>().Single();

    private int LiveServers(InpremAppFactory app) =>
        app.Services.GetRequiredService<JobStorage>().GetMonitoringApi().Servers().Count;

    [Fact]
    public async Task Jobs_App_Site_With_RunServer_Starts_A_Server_That_Runs_Jobs()
    {
        await using var app = Host("App", runServer: true);
        _ = app.Server;

        Assert.True(ServerHost(app).IsRunning);
        var announced = DateTime.UtcNow.AddSeconds(30); // the server registers itself in storage shortly after starting
        while (LiveServers(app) == 0 && DateTime.UtcNow < announced) await Task.Delay(200);
        Assert.Equal(1, LiveServers(app));

        var id = Guid.NewGuid().ToString("N");
        new BackgroundJobClient(app.Services.GetRequiredService<JobStorage>()).Enqueue(() => JobProbe.Run(id));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!JobProbe.Ran.ContainsKey(id) && DateTime.UtcNow < deadline) await Task.Delay(200);
        Assert.True(JobProbe.Ran.ContainsKey(id), "The enqueued job did not run.");
    }

    [Fact]
    public async Task Jobs_Admin_Site_Starts_No_Server()
    {
        await using var admin = Host("Admin");
        _ = admin.Server;

        Assert.False(ServerHost(admin).IsRunning);
        Assert.Equal(0, LiveServers(admin));
    }

    [Fact]
    public async Task Jobs_Admin_Site_Refuses_To_Start_With_RunServer()
    {
        await using var admin = Host("Admin", runServer: true);
        var error = Assert.ThrowsAny<Exception>(() => admin.Server);
        Assert.Contains("Jobs:RunServer must be false on the Admin site", error.ToString());
    }

    [Fact]
    public async Task Jobs_App_Site_Without_RunServer_Starts_No_Server_And_The_Old_Billing_Worker_Is_Gone()
    {
        await using var app = Host("App");
        _ = app.Server;

        Assert.False(app.Services.GetRequiredService<IOptions<JobsOptions>>().Value.RunServer); // appsettings.json default
        Assert.False(ServerHost(app).IsRunning);
        Assert.DoesNotContain(app.Services.GetServices<IHostedService>(), s => s is BillingBackgroundService);
    }

    [Fact]
    public async Task Jobs_Dashboard_Is_Read_Only_For_Provider_Owners_On_The_Admin_Site()
    {
        await using var admin = Host("Admin");
        _ = admin.Server;

        using var anonymous = admin.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/jobs")).StatusCode);

        // A provider user who is not an Owner: signed in, still no dashboard.
        var supportKey = await ProviderFixture.CreateEnrolledProviderUserAsync(admin.Services, _db.CreatePlatformDbContext, "support@jobs.test", ProviderRole.Support);
        using var support = admin.CreateClient();
        await ProviderFixture.SignInAsync(support, "support@jobs.test", supportKey, prefix: "");
        var refused = (await support.GetAsync("/jobs")).StatusCode;
        Assert.Contains(refused, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }); // Hangfire picks the code
        await ProviderForms.GetDocumentAsync(support, "/"); // refused for the role; the console session is untouched

        // An Owner signed in with two-factor sees it, read-only.
        var ownerKey = await ProviderFixture.CreateEnrolledProviderUserAsync(admin.Services, _db.CreatePlatformDbContext, "owner@jobs.test");
        using var owner = admin.CreateClient();
        await ProviderFixture.SignInAsync(owner, "owner@jobs.test", ownerKey, prefix: "");
        var page = await owner.GetAsync("/jobs");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Inprem jobs", await page.Content.ReadAsStringAsync());
        await ProviderForms.GetDocumentAsync(owner, "/"); // opening the dashboard keeps the console session

        var job = new BackgroundJobClient(admin.Services.GetRequiredService<JobStorage>()).Enqueue(() => JobProbe.Run("never-runs-on-admin"));
        var delete = await owner.PostAsync("/jobs/jobs/enqueued/delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["jobs[]"] = job }));
        Assert.False(delete.IsSuccessStatusCode, $"Dashboard accepted a change: {(int)delete.StatusCode}");
        Assert.Equal("Enqueued", admin.Services.GetRequiredService<JobStorage>().GetConnection().GetStateData(job).Name);
    }

    [Fact]
    public async Task Jobs_Dashboard_Is_Not_Served_By_The_App_Site()
    {
        await using var app = Host("App");
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Provider/jobs")).StatusCode);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
