using Inprem.Platform.Application.Time;
using InpremClockingApp.Hosting;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Provider.Organizations;

/// <summary>Helpers for the organization pages.</summary>
public static class OrganizationUi
{
    private static readonly string[] Common =
    {
        "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles",
        "America/Anchorage", "Pacific/Honolulu", "America/Puerto_Rico", "Europe/London", "Africa/Accra", "Africa/Lagos", "UTC",
    };

    private static readonly Lazy<IReadOnlyList<(string Id, string Label)>> Zones = new(BuildZones);

    /// <summary>IANA time zones for the dropdown: the common ones first, then every other one known to this server.</summary>
    public static IReadOnlyList<(string Id, string Label)> TimeZones => Zones.Value;

    private static IReadOnlyList<(string Id, string Label)> BuildZones()
    {
        var ids = new List<string>();
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            if (zone.HasIanaId) ids.Add(zone.Id);
            else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana)) ids.Add(iana);
        }

        var rest = ids.Distinct(StringComparer.Ordinal).Where(id => !Common.Contains(id)).OrderBy(id => id, StringComparer.Ordinal);
        return Common.Concat(rest)
            .Select(id => (Id: id, Zone: TimeZoneResolver.TryFind(id)))
            .Where(z => z.Zone != null)
            .Select(z => (z.Id, $"{z.Id} (UTC{Offset(z.Zone!)})"))
            .ToList();
    }

    private static string Offset(TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(DateTime.UtcNow);
        return (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm");
    }

    /// <summary>
    /// Base of App-site links in emails: Links:AppBaseUrl when configured (OrganizationMail reads
    /// it); otherwise, on a combined site (SiteMode All), this request's own address.
    /// </summary>
    public static string? AppLinkBase(HttpContext context)
    {
        var site = context.RequestServices.GetRequiredService<IOptions<SiteOptions>>().Value;
        return site.SiteMode == SiteMode.All ? $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}" : null;
    }
}
