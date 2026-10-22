using System.Text.Json;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Hosting;

/// <summary>How the Content-Security-Policy is sent (step 7.6).</summary>
public enum CspMode
{
    /// <summary>No policy header.</summary>
    Off,

    /// <summary><c>Content-Security-Policy-Report-Only</c>: browsers block nothing and report what they would block.</summary>
    ReportOnly,

    /// <summary><c>Content-Security-Policy</c>: browsers block what the policy does not allow.</summary>
    Enforce,
}

/// <summary>Configuration section <c>SecurityHeaders</c> (Phase 7 step 7.6).</summary>
public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    /// <summary>ReportOnly until the reports from the test site are clean; then Enforce.</summary>
    public CspMode ContentSecurityPolicy { get; set; } = CspMode.ReportOnly;
}

/// <summary>
/// Security headers on every response of both sites (step 7.6). HSTS is set by <c>UseHsts</c>
/// outside Development (Program.cs). A header a page has set itself is kept (the console sends
/// <c>Referrer-Policy: no-referrer</c>).
/// </summary>
public static class SecurityHeaders
{
    public const string ReportPath = "/csp-report";

    /// <summary>
    /// What the pages use today: the app's own files; the approved pages' inline scripts, styles and
    /// event handlers; Google Fonts (Material Icons); jQuery validation from cdnjs (Identity pages);
    /// Cloudflare Turnstile (demo website); data: and blob: for images and report downloads.
    /// </summary>
    public static readonly string Policy = string.Join("; ",
        "default-src 'self'",
        "script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://challenges.cloudflare.com",
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
        "font-src 'self' https://fonts.gstatic.com data:",
        "img-src 'self' data: blob:",
        "connect-src 'self'",
        "frame-src 'self' blob: https://challenges.cloudflare.com",
        "object-src 'none'",
        "base-uri 'self'",
        "frame-ancestors 'self'",
        "report-uri " + ReportPath);

    public static readonly IReadOnlyDictionary<string, string> Fixed = new Dictionary<string, string>
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin",
        ["X-Frame-Options"] = "SAMEORIGIN",
        ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()",
    };
}

/// <summary>Adds the headers just before a response starts, and receives the browsers' policy reports.</summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>At most this many reports are logged per minute (a page with a problem reports on every view).</summary>
    public const int ReportsLoggedPerMinute = 30;
    private const int MaxReportBytes = 16 * 1024;

    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _log;
    private readonly string? _policyHeader;
    private long _window;
    private int _logged;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeadersOptions> options, ILogger<SecurityHeadersMiddleware> log)
    {
        _next = next;
        _log = log;
        _policyHeader = options.Value.ContentSecurityPolicy switch
        {
            CspMode.Enforce => "Content-Security-Policy",
            CspMode.ReportOnly => "Content-Security-Policy-Report-Only",
            _ => null,
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals(SecurityHeaders.ReportPath, StringComparison.OrdinalIgnoreCase))
        {
            await ReceiveReportAsync(context);
            return;
        }

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            foreach (var (name, value) in SecurityHeaders.Fixed)
                if (!headers.ContainsKey(name)) headers[name] = value;
            if (_policyHeader != null && !headers.ContainsKey(_policyHeader)) headers[_policyHeader] = SecurityHeaders.Policy;
            return Task.CompletedTask;
        });
        await _next(context);
    }

    /// <summary>
    /// A browser's report (<c>application/csp-report</c>): logged as a warning with the directive and
    /// the blocked and page addresses without query strings (they can hold tokens). Always 204.
    /// </summary>
    private async Task ReceiveReportAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        if (!HttpMethods.IsPost(context.Request.Method) || context.Request.ContentLength > MaxReportBytes) return;

        var buffer = new byte[MaxReportBytes];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = await context.Request.Body.ReadAsync(buffer.AsMemory(read), context.RequestAborted)) > 0) read += n;

        string? directive = null, blocked = null, page = null;
        try
        {
            using var json = JsonDocument.Parse(buffer.AsMemory(0, read));
            if (json.RootElement.TryGetProperty("csp-report", out var report))
            {
                directive = Text(report, "effective-directive") ?? Text(report, "violated-directive");
                blocked = WithoutQuery(Text(report, "blocked-uri"));
                page = WithoutQuery(Text(report, "document-uri"));
            }
        }
        catch (JsonException)
        {
            return;
        }

        if (directive == null || !TakeLogSlot()) return;
        _log.LogWarning("Content-Security-Policy report: {Directive} blocked {Blocked} on {Page}.", directive, blocked ?? "(none)", page ?? "(none)");
    }

    private bool TakeLogSlot()
    {
        var minute = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
        if (Interlocked.Exchange(ref _window, minute) != minute) Interlocked.Exchange(ref _logged, 0);
        return Interlocked.Increment(ref _logged) <= ReportsLoggedPerMinute;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>"https://host/path?code=…" becomes "https://host/path"; keywords such as "inline" are kept.</summary>
    public static string? WithoutQuery(string? address)
    {
        if (string.IsNullOrEmpty(address)) return null;
        var cut = address.IndexOfAny(new[] { '?', '#' });
        var value = cut < 0 ? address : address[..cut];
        return value.Length > 300 ? value[..300] : value;
    }
}

/// <summary>Puts the headers first in the pipeline, so error pages, redirects and static files get them too.</summary>
internal sealed class SecurityHeadersStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        next(app);
    };
}
