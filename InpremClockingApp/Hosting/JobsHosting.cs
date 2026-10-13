using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using InpremClockingApp.Provider;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Hosting;

/// <summary>Configuration section <c>Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Run the Hangfire job server in this process. Only on the App site (D6); refused on an Admin-only site.</summary>
    public bool RunServer { get; set; }
}

/// <summary>
/// Background jobs (Phase 0 step 0.8, decision D6): Hangfire with SQL Server storage in schema
/// <c>hangfire</c>.
/// <list type="bullet">
/// <item>Every site registers the storage (to enqueue jobs and read their state).</item>
/// <item>The job server runs only when <c>Jobs:RunServer</c> is true on a site that serves the App.</item>
/// <item>The Admin site shows the dashboard read-only, to Provider Owners only.</item>
/// <item>The app never creates or upgrades Hangfire's tables (D12): <c>scripts/migrations/hangfire-up.idempotent.sql</c>.</item>
/// </list>
/// </summary>
public static class JobsHosting
{
    public const string Schema = "hangfire";

    /// <summary>The dashboard: /jobs on the Admin site, /Provider/jobs with SiteMode All.</summary>
    public static string DashboardPath(SiteOptions site) => site.SiteMode == SiteMode.Admin ? "/jobs" : "/Provider/jobs";

    public static WebApplicationBuilder AddJobs(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<JobsOptions>().Bind(builder.Configuration.GetSection(JobsOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JobsOptions>, JobsOptionsValidator>();

        // The configuration callback runs when storage is first needed, so it sees the final
        // configuration (including per-environment and test settings).
        services.AddHangfire((sp, configuration) => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection"), StorageOptions()));

        services.AddHostedService<JobServerHost>();
        return builder;
    }

    public static SqlServerStorageOptions StorageOptions() => new()
    {
        SchemaName = Schema,
        PrepareSchemaIfNecessary = false,
        SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance,
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
    };

    /// <summary>The read-only jobs dashboard, on deployments that serve the Admin site (on its host when one is set).</summary>
    public static IEndpointRouteBuilder MapJobsDashboard(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.ServiceProvider.GetRequiredService<IOptions<SiteOptions>>().Value;
        if (!site.ServesAdmin) return endpoints;

        var dashboard = endpoints.MapHangfireDashboard(DashboardPath(site), new DashboardOptions
        {
            DashboardTitle = "Inprem jobs",
            AppPath = site.SiteMode == SiteMode.Admin ? "/" : "/Provider",
            IsReadOnlyFunc = _ => true,
            Authorization = Array.Empty<IDashboardAuthorizationFilter>(),
            AsyncAuthorization = new[] { new ProviderOwnerDashboardFilter() },
            DisplayStorageConnectionString = false,
        });
        dashboard.WithMetadata(ProviderEndpointMetadata.Instance);
        if (site.HostFor(SiteMode.Admin) is { } host) dashboard.RequireHost(host);
        return endpoints;
    }
}

public sealed class JobsOptionsValidator : IValidateOptions<JobsOptions>
{
    private readonly IOptions<SiteOptions> _site;

    public JobsOptionsValidator(IOptions<SiteOptions> site) => _site = site;

    public ValidateOptionsResult Validate(string? name, JobsOptions options) =>
        options.RunServer && _site.Value.SiteMode == SiteMode.Admin
            ? ValidateOptionsResult.Fail("Jobs:RunServer must be false on the Admin site: jobs run only on the App site (decision D6).")
            : ValidateOptionsResult.Success;
}

/// <summary>Starts the Hangfire job server at startup only when this site may run jobs.</summary>
public sealed class JobServerHost : IHostedService, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly JobsOptions _jobs;
    private readonly SiteOptions _site;
    private readonly ILogger<JobServerHost> _logger;
    private BackgroundJobServer? _server;

    public JobServerHost(IServiceProvider services, IOptions<JobsOptions> jobs, IOptions<SiteOptions> site, ILogger<JobServerHost> logger)
    {
        _services = services;
        _jobs = jobs.Value;
        _site = site.Value;
        _logger = logger;
    }

    /// <summary>Whether this process runs jobs.</summary>
    public bool IsRunning => _server != null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_jobs.RunServer || !_site.ServesApp)
        {
            _logger.LogInformation("Hangfire job server not started (Jobs:RunServer = {RunServer}, SiteMode = {SiteMode}).", _jobs.RunServer, _site.SiteMode);
            return Task.CompletedTask;
        }

        _server = new BackgroundJobServer(new BackgroundJobServerOptions
        {
            ServerName = $"{Environment.MachineName}:{Environment.ProcessId}:app",
            Activator = _services.GetService<JobActivator>() ?? new Hangfire.AspNetCore.AspNetCoreJobActivator(_services.GetRequiredService<IServiceScopeFactory>()),
        }, _services.GetRequiredService<JobStorage>());
        _logger.LogInformation("Hangfire job server started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server == null) return;
        _server.SendStop();
        await _server.WaitForShutdownAsync(cancellationToken);
    }

    public void Dispose() => _server?.Dispose();
}

/// <summary>Jobs dashboard: active Provider Owners, signed in to the console with two-factor (design 2.2, step 0.8).</summary>
public sealed class ProviderOwnerDashboardFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var check = http.RequestServices.GetRequiredService<ProviderAccessCheck>();
        var (outcome, user) = await check.CheckAsync(http.User, ProviderAccessLevel.Console);
        return outcome == ProviderAccessOutcome.Allowed && user is { IsActiveOwner: true };
    }
}
