using Microsoft.Extensions.Options;

namespace InpremClockingApp.Hosting;

/// <summary>
/// Configuration section <c>LegacyRedirect</c> (domain cutover step C.2, C3-11). Off by default.
/// When on, requests on one of <see cref="FromHosts"/> (the old address, e.g. inpremclockin.org)
/// for a known application path get a permanent redirect (301) to the same path and query on
/// <see cref="TargetBaseUrl"/> (e.g. https://app.inpremclockin.org). Other paths are left alone:
/// they belong to the marketing website.
/// </summary>
public sealed class LegacyRedirectOptions
{
    public const string SectionName = "LegacyRedirect";

    public bool Enabled { get; set; }

    /// <summary>The old host names, e.g. inpremclockin.org and www.inpremclockin.org (no port, no scheme).</summary>
    public string[] FromHosts { get; set; } = Array.Empty<string>();

    /// <summary>Where they go, e.g. https://app.inpremclockin.org (https, no path).</summary>
    public string? TargetBaseUrl { get; set; }
}

public sealed class LegacyRedirectOptionsValidator : IValidateOptions<LegacyRedirectOptions>
{
    public ValidateOptionsResult Validate(string? name, LegacyRedirectOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var errors = new List<string>();
        if (options.FromHosts.Length == 0 || options.FromHosts.Any(h => string.IsNullOrWhiteSpace(h) || Uri.CheckHostName(h.Trim()) is UriHostNameType.Unknown))
            errors.Add("LegacyRedirect:FromHosts must list the old host names, e.g. inpremclockin.org (no scheme, no port, no path).");
        if (!Uri.TryCreate(options.TargetBaseUrl, UriKind.Absolute, out var target) || target.Scheme != Uri.UriSchemeHttps
            || target.AbsolutePath != "/" || !string.IsNullOrEmpty(target.Query) || options.TargetBaseUrl!.EndsWith('/'))
            errors.Add("LegacyRedirect:TargetBaseUrl must be an https address with no path, e.g. https://app.inpremclockin.org.");
        else if (options.FromHosts.Any(h => string.Equals(h.Trim(), target.Host, StringComparison.OrdinalIgnoreCase)))
            errors.Add("LegacyRedirect:TargetBaseUrl must not be one of LegacyRedirect:FromHosts (it would redirect to itself).");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// The application paths that move to app.inpremclockin.org, by their first path segment
/// (case-insensitive). "Staff*" covers every segment starting with "staff": /Staff, /StaffAttendance,
/// /staff-clockin/12, ... The root "/" is not in the list: it becomes the website's home page.
/// A test checks that every App-site route is covered, and that
/// docs/multitenancy/runbooks/root-redirects.md lists exactly these rules.
/// </summary>
public static class LegacyPaths
{
    public static readonly IReadOnlyList<string> Rules = new[]
    {
        "Account", "api", "BackOffice", "Billing", "Error", "HoursWorked", "Identity", "Index", "Privacy",
        "Settings", "Staff*", "User", "Volunteer*", "webhooks",
    };

    public static bool IsApplicationPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value) || value == "/") return false;
        var first = value.TrimStart('/').Split('/', 2)[0];
        if (first.Length == 0) return false;
        foreach (var rule in Rules)
        {
            var matches = rule.EndsWith('*')
                ? first.StartsWith(rule[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(first, rule, StringComparison.OrdinalIgnoreCase);
            if (matches) return true;
        }
        return false;
    }
}

/// <summary>Answers the old host's application paths with 301 to the new host, before anything else runs.</summary>
public sealed class LegacyRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _fromHosts;
    private readonly string? _target;

    public LegacyRedirectMiddleware(RequestDelegate next, IOptions<LegacyRedirectOptions> options)
    {
        _next = next;
        var value = options.Value;
        _fromHosts = value.Enabled ? new HashSet<string>(value.FromHosts.Select(h => h.Trim()), StringComparer.OrdinalIgnoreCase) : new();
        _target = value.Enabled ? value.TargetBaseUrl : null;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (_target == null || !_fromHosts.Contains(request.Host.Host) || !LegacyPaths.IsApplicationPath(request.Path)) return _next(context);

        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = _target + request.PathBase + request.Path + request.QueryString;
        return Task.CompletedTask;
    }
}

/// <summary>Puts the redirect first in the pipeline (before HTTPS redirection, so an old http:// link takes one hop).</summary>
internal sealed class LegacyRedirectStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<LegacyRedirectMiddleware>();
        next(app);
    };
}
