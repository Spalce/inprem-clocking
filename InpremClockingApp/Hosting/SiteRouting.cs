using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Hosting;

/// <summary>
/// Which site a page or controller belongs to: the Provider area is the Admin site; everything
/// else (the approved pages, the Identity pages, Areas/Billing, the website API and webhooks)
/// is the App site.
/// </summary>
public static class SiteMap
{
    public const string ProviderArea = "Provider";

    public static SiteMode SiteOf(string? area) =>
        string.Equals(area, ProviderArea, StringComparison.OrdinalIgnoreCase) ? SiteMode.Admin : SiteMode.App;

    public static bool Serves(this SiteOptions options, SiteMode site) =>
        site == SiteMode.Admin ? options.ServesAdmin : options.ServesApp;

    /// <summary>The host a site is restricted to, or null when no host is configured for it.</summary>
    public static string? HostFor(this SiteOptions options, SiteMode site)
    {
        var host = site == SiteMode.Admin ? options.AdminHost : options.AppHost;
        return string.IsNullOrWhiteSpace(host) ? null : host.Trim();
    }
}

/// <summary>
/// Razor Pages: a page of a site this deployment does not serve gets no route at all (404); a
/// page of a served site is restricted to that site's host when one is configured (404 on any
/// other host).
/// </summary>
public sealed class SitePageRouteConvention : IPageRouteModelConvention
{
    private readonly SiteOptions _site;

    public SitePageRouteConvention(SiteOptions site) => _site = site;

    public void Apply(PageRouteModel model)
    {
        var site = SiteMap.SiteOf(model.AreaName);
        if (!_site.Serves(site))
        {
            model.Selectors.Clear();
            return;
        }

        // On a dedicated Admin site the console is the whole site: /setup, /Login, ... rather than
        // /Provider/setup. With SiteMode All it stays under /Provider, next to the App pages.
        if (site == SiteMode.Admin && _site.SiteMode == SiteMode.Admin)
        {
            foreach (var selector in model.Selectors.Where(s => s.AttributeRouteModel?.Template != null))
            {
                selector.AttributeRouteModel!.Template = WithoutAreaPrefix(selector.AttributeRouteModel.Template!);
            }
        }

        var host = _site.HostFor(site);
        if (host == null) return;

        foreach (var selector in model.Selectors)
        {
            selector.EndpointMetadata.Add(new HostAttribute(host));
        }
    }

    private static string WithoutAreaPrefix(string template)
    {
        if (string.Equals(template, SiteMap.ProviderArea, StringComparison.OrdinalIgnoreCase)) return "";
        var prefix = SiteMap.ProviderArea + "/";
        return template.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? template[prefix.Length..] : template;
    }
}

/// <summary>Controllers: same rule as <see cref="SitePageRouteConvention"/>.</summary>
public sealed class SiteControllerConvention : IApplicationModelConvention
{
    private readonly SiteOptions _site;

    public SiteControllerConvention(SiteOptions site) => _site = site;

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.ToList())
        {
            controller.RouteValues.TryGetValue("area", out var area);
            var site = SiteMap.SiteOf(area);

            if (!_site.Serves(site))
            {
                application.Controllers.Remove(controller);
                continue;
            }

            var host = _site.HostFor(site);
            if (host == null) continue;

            foreach (var selector in controller.Selectors.Concat(controller.Actions.SelectMany(a => a.Selectors)))
            {
                selector.EndpointMetadata.Add(new HostAttribute(host));
            }
        }
    }
}

/// <summary>Adds the two conventions once the validated <see cref="SiteOptions"/> are known.</summary>
internal sealed class ConfigureSiteRouting :
    IConfigureOptions<RazorPagesOptions>,
    IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>
{
    private readonly SiteOptions _site;

    public ConfigureSiteRouting(IOptions<SiteOptions> site) => _site = site.Value;

    public void Configure(RazorPagesOptions options) => options.Conventions.Add(new SitePageRouteConvention(_site));

    public void Configure(Microsoft.AspNetCore.Mvc.MvcOptions options) => options.Conventions.Add(new SiteControllerConvention(_site));
}
