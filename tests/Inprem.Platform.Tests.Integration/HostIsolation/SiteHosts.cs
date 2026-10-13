using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Inprem.Platform.Tests.Integration.HostIsolation;

/// <summary>
/// Stand-in for the Provider Console until Phase 1 adds it: a controller in the Provider area,
/// so the site rules treat it exactly as they will treat the real console.
/// </summary>
[Area("Provider")]
[Route("provider-probe")]
[AllowAnonymous]
public class ProviderProbeController : ControllerBase
{
    [HttpGet]
    public string Get() => "provider";
}

/// <summary>A controller outside any area: an App-site endpoint, like the approved API controllers.</summary>
[Route("app-probe")]
[AllowAnonymous]
public class AppProbeController : ControllerBase
{
    [HttpGet]
    public string Get() => "app";
}

internal static class SiteHosts
{
    /// <summary>A host for one site configuration, with the probe controllers added.</summary>
    public static InpremAppFactory Factory(string connectionString, string? mode, string? appHost = null, string? adminHost = null,
        bool persistKeys = false, IDictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Site:SiteMode"] = mode,
            ["Site:AppHost"] = appHost ?? "",
            ["Site:AdminHost"] = adminHost ?? "",
        };
        if (extra != null)
        {
            foreach (var (key, value) in extra) settings[key] = value;
        }

        return new InpremAppFactory(connectionString, settings, persistKeys,
            services => services.AddMvcCore().AddApplicationPart(typeof(ProviderProbeController).Assembly));
    }

    public static HttpClient Client(this InpremAppFactory factory, string host = "localhost") =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{host}"),
            AllowAutoRedirect = false,
        });

    /// <summary>Every routed endpoint the host serves, as (route pattern, area or null).</summary>
    public static List<(string Pattern, string? Area)> Endpoints(this InpremAppFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (e.RoutePattern.RawText ?? "", Area(e)))
            .ToList();

    private static string? Area(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RequiredValues.TryGetValue("area", out var area) && area is string text && text.Length > 0 ? text : null;

    /// <summary>A concrete URL for a route pattern (parameters filled with 1), or null when it cannot be built.</summary>
    public static string? SampleUrl(string pattern)
    {
        var url = System.Text.RegularExpressions.Regex.Replace(pattern, @"\{[^}]+\}", "1");
        return url.Contains('*') ? null : "/" + url.TrimStart('/');
    }
}
