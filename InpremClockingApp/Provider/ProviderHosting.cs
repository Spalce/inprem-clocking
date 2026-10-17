using Inprem.Platform.Application.Provider;
using InpremClockingApp.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
        services.AddScoped<ProviderAccessCheck>();
        services.AddScoped<ProviderAccessFilter>();
        services.AddScoped<ProviderSession>();
        services.AddScoped<Inprem.Platform.Application.Organizations.IOrganizationStore, Organizations.OrganizationStore>();
        services.AddScoped<Inprem.Platform.Application.Organizations.IOrganizationMail, Organizations.OrganizationMail>();

        // Roles (step 1.1): ProviderSupport / ProviderBilling / ProviderOwner.
        services.AddScoped<IAuthorizationHandler, ProviderRoleHandler>();
        services.Configure<AuthorizationOptions>(ProviderPolicies.Add);

        // Admin site IP allow-list (D17), validated at startup.
        services.AddSingleton<IValidateOptions<AdminSiteOptions>, AdminSiteOptionsValidator>();
        services.AddOptions<AdminSiteOptions>().Bind(builder.Configuration.GetSection(AdminSiteOptions.SectionName)).ValidateOnStart();

        // Provider sessions: 8 h / 30 min limits on top of Identity's checks; console requests that
        // need a sign-in or lack the role go to the console's own login and access-denied pages.
        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, cookie =>
        {
            var validate = cookie.Events.OnValidatePrincipal;
            cookie.Events.OnValidatePrincipal = async context =>
            {
                await validate(context);
                if (context.Principal != null) await ProviderSession.ValidateAsync(context);
            };
            var toLogin = cookie.Events.OnRedirectToLogin;
            cookie.Events.OnRedirectToLogin = context =>
                ProviderSession.IsConsoleRequest(context.HttpContext) ? RedirectToConsolePage(context, "/Login") : toLogin(context);
            var toDenied = cookie.Events.OnRedirectToAccessDenied;
            cookie.Events.OnRedirectToAccessDenied = context =>
                ProviderSession.IsConsoleRequest(context.HttpContext) ? RedirectToConsolePage(context, "/AccessDenied") : toDenied(context);
        });

        // Every Provider page goes through the access gate and needs at least a provider role
        // (pages open to everyone say [AllowAnonymous]), including pages added later. These are
        // security forms, so antiforgery tokens are checked here (closer than the app-wide
        // IgnoreAntiforgeryToken convention of the approved pages, so this one applies).
        services.Configure<RazorPagesOptions>(options =>
        {
            options.Conventions.AuthorizeAreaFolder(SiteMap.ProviderArea, "/", ProviderPolicies.Support);
            options.Conventions.AddAreaFolderApplicationModelConvention(SiteMap.ProviderArea, "/", model =>
            {
                model.Filters.Add(new ServiceFilterAttribute(typeof(ProviderAccessFilter)));
                // A page that opts out explicitly (only Logout: a stale form must still sign out) keeps its choice.
                if (!model.HandlerType.IsDefined(typeof(IgnoreAntiforgeryTokenAttribute), inherit: true))
                    model.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            });
        });

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

        var health = endpoints.MapHealthChecks(HealthPath).WithMetadata(ProviderEndpointMetadata.Instance);
        if (site.HostFor(SiteMode.Admin) is { } host) health.RequireHost(host);
        return endpoints;
    }

    private static Task RedirectToConsolePage(RedirectContext<CookieAuthenticationOptions> context, string page)
    {
        var links = context.HttpContext.RequestServices.GetRequiredService<LinkGenerator>();
        var target = links.GetPathByPage(context.HttpContext, page, values: new { area = SiteMap.ProviderArea }) ?? "/";
        if (IsAjax(context.Request))
        {
            context.Response.StatusCode = page == "/Login" ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
        }
        else
        {
            context.Response.Redirect(target);
        }
        return Task.CompletedTask;
    }

    private static bool IsAjax(HttpRequest request) =>
        string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);

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
