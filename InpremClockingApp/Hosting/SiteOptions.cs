using Microsoft.Extensions.Options;

namespace InpremClockingApp.Hosting;

/// <summary>Which site this deployment serves (decision D8: one build, deployed as two sites).</summary>
public enum SiteMode
{
    /// <summary>Back office, kiosk, website API and webhooks (app.inpremclockin.org).</summary>
    App,

    /// <summary>Provider Console only (admin.inpremclockin.org).</summary>
    Admin,

    /// <summary>Both, on one host or two (local development, or a test site without subdomains: D9).</summary>
    All,
}

/// <summary>Configuration section <c>Site</c>. Validated at startup.</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    public SiteMode? SiteMode { get; set; }

    /// <summary>Host name of the App site, e.g. <c>app.inpremclockin.org</c>. Empty: any host.</summary>
    public string? AppHost { get; set; }

    /// <summary>Host name of the Admin site, e.g. <c>admin.inpremclockin.org</c>. Empty: any host.</summary>
    public string? AdminHost { get; set; }

    /// <summary>Origins of the marketing website, for CORS and return-URL checks (Phase 3).</summary>
    public string[] PublicWebsiteOrigins { get; set; } = Array.Empty<string>();

    public bool ServesApp => SiteMode is Hosting.SiteMode.App or Hosting.SiteMode.All;

    public bool ServesAdmin => SiteMode is Hosting.SiteMode.Admin or Hosting.SiteMode.All;
}

public sealed class SiteOptionsValidator : IValidateOptions<SiteOptions>
{
    public ValidateOptionsResult Validate(string? name, SiteOptions options)
    {
        var errors = new List<string>();

        if (options.SiteMode is null)
        {
            errors.Add("Site:SiteMode is required (App, Admin or All).");
        }

        CheckHost(options.AppHost, "Site:AppHost", errors);
        CheckHost(options.AdminHost, "Site:AdminHost", errors);

        if (!string.IsNullOrWhiteSpace(options.AppHost) && !string.IsNullOrWhiteSpace(options.AdminHost) &&
            string.Equals(options.AppHost.Trim(), options.AdminHost.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Site:AppHost and Site:AdminHost must be different hosts.");
        }

        foreach (var origin in options.PublicWebsiteOrigins)
        {
            var valid = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                        && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && !origin.EndsWith('/');
            if (!valid)
            {
                errors.Add($"Site:PublicWebsiteOrigins entry '{origin}' must be an origin such as https://inpremclockin.org (https, no path, no trailing slash).");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void CheckHost(string? host, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(host)) return;

        // Host name with an optional port, e.g. app.inpremclockin.org or localhost:5001.
        var value = host.Trim();
        var colon = value.LastIndexOf(':');
        var name = colon > 0 ? value[..colon] : value;
        var portOk = colon <= 0 || int.TryParse(value[(colon + 1)..], out var port) && port is > 0 and < 65536;

        if (!portOk || Uri.CheckHostName(name) is UriHostNameType.Unknown || value.Contains('/'))
        {
            errors.Add($"{key} '{host}' must be a host name (optionally with :port) only, e.g. app.inpremclockin.org.");
        }
    }
}
