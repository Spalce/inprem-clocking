using InpremClockingApp.Hosting;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.PublicApi;

/// <summary>Configuration section <c>Demo</c> (Phase 3 step 3.6, D29).</summary>
public sealed class DemoSiteOptions
{
    public const string Section = "Demo";

    /// <summary>Serve the demo website at /demo-site (off by default; never in production).</summary>
    public bool EnableDemoSite { get; set; }

    /// <summary>Cloudflare Turnstile site key for the demo's widget (public; e.g. Cloudflare's test key).</summary>
    public string? CaptchaSiteKey { get; set; }

    /// <summary>The CAPTCHA answer the demo sends when there is no widget (tests, or Turnstile's test secret).</summary>
    public string? CaptchaTestToken { get; set; }
}

/// <summary>
/// The demo website (D29): plain HTML in <c>wwwroot/demo-site</c> that uses only the public API,
/// until the web team's site exists. When it is switched off, or on an Admin-only site, every
/// <c>/demo-site</c> address answers 404 (the files are never served).
/// </summary>
public static class DemoSite
{
    public const string Path = "/demo-site";

    public static IServiceCollection AddDemoSite(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DemoSiteOptions>().Bind(configuration.GetSection(DemoSiteOptions.Section));
        return services;
    }

    /// <summary>Before UseStaticFiles.</summary>
    public static IApplicationBuilder UseDemoSiteGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments(Path, StringComparison.OrdinalIgnoreCase) && !Enabled(context.RequestServices))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });

    public static IEndpointRouteBuilder MapDemoSite(this IEndpointRouteBuilder endpoints)
    {
        if (!Enabled(endpoints.ServiceProvider)) return endpoints;
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<DemoSiteOptions>>().Value;
        endpoints.MapGet(Path, () => Results.Redirect(Path + "/index.html"));
        // The billing link goes to {Links:WebsiteBaseUrl}/billing?code=...; the demo's page is billing.html.
        endpoints.MapGet(Path + "/billing", (HttpContext http) => Results.Redirect(Path + "/billing.html" + http.Request.QueryString));
        endpoints.MapGet(Path + "/config.json", () => Results.Json(new { captchaSiteKey = options.CaptchaSiteKey, captchaTestToken = options.CaptchaTestToken }));
        return endpoints;
    }

    private static bool Enabled(IServiceProvider services) =>
        services.GetRequiredService<IOptions<DemoSiteOptions>>().Value.EnableDemoSite && services.GetRequiredService<IOptions<SiteOptions>>().Value.ServesApp;
}
