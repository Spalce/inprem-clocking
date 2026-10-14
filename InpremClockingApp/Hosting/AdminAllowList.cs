using System.Net;
using InpremClockingApp.Provider;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Hosting;

/// <summary>Configuration section <c>AdminSite</c> (decision D17).</summary>
public sealed class AdminSiteOptions
{
    public const string SectionName = "AdminSite";

    /// <summary>
    /// Addresses allowed to reach the Admin site, as CIDR ranges or single addresses
    /// (e.g. 203.0.113.0/24, 198.51.100.7). Empty: everyone (testing only; set before production).
    /// </summary>
    public string[] AllowedNetworks { get; set; } = Array.Empty<string>();
}

public sealed class AdminSiteOptionsValidator : IValidateOptions<AdminSiteOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminSiteOptions options)
    {
        var invalid = options.AllowedNetworks.Where(n => AdminAllowList.Parse(n) == null).ToList();
        return invalid.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"AdminSite:AllowedNetworks has entries that are not an address or CIDR range: {string.Join(", ", invalid)}.");
    }
}

/// <summary>
/// IP allow-list for the Admin site (step 1.1, D17): requests from outside
/// <c>AdminSite:AllowedNetworks</c> get 403 before anything else runs. It covers every request of a
/// dedicated Admin site, and the console's requests on a combined site (SiteMode All). Off when the
/// list is empty, and in local development.
/// </summary>
public sealed class AdminAllowList
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<IPNetwork> _networks;
    private readonly SiteOptions _site;
    private readonly bool _enabled;
    private readonly ILogger<AdminAllowList> _logger;

    public AdminAllowList(RequestDelegate next, IOptions<AdminSiteOptions> options, IOptions<SiteOptions> site, IWebHostEnvironment environment, ILogger<AdminAllowList> logger)
    {
        _next = next;
        _networks = options.Value.AllowedNetworks.Select(Parse).OfType<IPNetwork>().ToList();
        _site = site.Value;
        _enabled = _networks.Count > 0 && !environment.IsDevelopment() && site.Value.ServesAdmin;
        _logger = logger;
    }

    public static IPNetwork? Parse(string value)
    {
        var text = value.Trim();
        if (IPNetwork.TryParse(text, out var network)) return network;
        return IPAddress.TryParse(text, out var address)
            ? new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128)
            : null;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_enabled && IsAdminSiteRequest(context) && !IsAllowed(context.Connection.RemoteIpAddress))
        {
            _logger.LogWarning("Admin site request from {Address} refused by the allow-list.", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    private bool IsAdminSiteRequest(HttpContext context) =>
        _site.SiteMode == SiteMode.Admin || ProviderSession.IsConsoleRequest(context);

    private bool IsAllowed(IPAddress? address)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return _networks.Any(n => n.Contains(address));
    }
}
