using Inprem.Platform.Application.Provider;
using InpremClockingApp.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Provider;

/// <summary>Provider users, bootstrap and recovery (Phase 0 step 0.7, design section 5).</summary>
public static class ProviderHosting
{
    public const string HealthPath = "/health";

    public static WebApplicationBuilder AddProviderSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        // Server-only settings (user-secrets / host configuration; never in the repository).
        services.AddSingleton(sp => ReadOptions(sp.GetRequiredService<IConfiguration>()));
        services.AddScoped<IAccountGateway, AccountGateway>();
        services.AddScoped<ProviderSecurityService>();
        services.AddScoped<ProviderAccessFilter>();

        // Every Provider page goes through the access gate, including pages added later. These are
        // security forms, so antiforgery tokens are checked here (closer than the app-wide
        // IgnoreAntiforgeryToken convention of the approved pages, so this one applies).
        services.Configure<RazorPagesOptions>(options =>
            options.Conventions.AddAreaFolderApplicationModelConvention(SiteMap.ProviderArea, "/", model =>
            {
                model.Filters.Add(new ServiceFilterAttribute(typeof(ProviderAccessFilter)));
                model.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            }));

        // Go-live check (D16): Degraded while fewer than two Provider Owners are active with two-factor.
        services.AddHealthChecks().AddCheck<ProviderOwnersHealthCheck>("provider-owners");
        return builder;
    }

    /// <summary>
    /// <c>/health</c> on deployments that serve the Admin site (on its host when one is set):
    /// Degraded while the D16 minimum of Provider Owners is not met. The body is only the status.
    /// </summary>
    public static IEndpointRouteBuilder MapProviderHealth(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.ServiceProvider.GetRequiredService<IOptions<SiteOptions>>().Value;
        if (!site.ServesAdmin) return endpoints;

        var health = endpoints.MapHealthChecks(HealthPath);
        if (site.HostFor(SiteMode.Admin) is { } host) health.RequireHost(host);
        return endpoints;
    }

    public static ProviderSecurityOptions ReadOptions(IConfiguration configuration)
    {
        var approvers = configuration.GetSection("Security:RecoveryApprovers");
        var list = approvers.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(approvers.Value))
        {
            // A single setting (e.g. an environment variable): comma- or semicolon-separated.
            list = approvers.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        return new ProviderSecurityOptions
        {
            BootstrapCodeHash = configuration["Bootstrap:CodeHash"],
            BreakGlassCodeHash = configuration["Security:BreakGlassCodeHash"],
            RecoveryApprovers = list.ToArray(),
            SecurityMailbox = configuration["Security:Mailbox"],
            AdminBaseUrl = configuration["Links:AdminBaseUrl"],
        };
    }
}

/// <summary>Degraded (not Unhealthy: the site works) while fewer than two Provider Owners are active with two-factor.</summary>
public sealed class ProviderOwnersHealthCheck : IHealthCheck
{
    private readonly ProviderSecurityService _provider;

    public ProviderOwnersHealthCheck(ProviderSecurityService provider) => _provider = provider;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _provider.GetOwnerStatusAsync(cancellationToken);
        return status.MeetsMinimum
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"{status.EnrolledOwners} of the required {OwnerStatus.Minimum} Provider Owners are active with two-factor.");
    }
}
