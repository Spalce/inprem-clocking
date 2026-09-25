namespace InpremClockingApp.Helpers;

/// <summary>
/// Single source of truth for the organization's wall-clock timezone.
/// All timestamps are stored in UTC; this converts to/from the org's
/// local time (Columbus, Ohio) at the edges - for display, and for
/// interpreting locally-scoped values such as "today" or an admin-entered
/// clock time - so every part of the app agrees on what "now" and "today" mean.
/// </summary>
public static class OrgClock
{
    private static TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static void Configure(IConfiguration configuration)
    {
        var id = configuration["OrgTimeZone"];
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "Eastern Standard Time" : id);
    }

    public static TimeZoneInfo TimeZone => _timeZone;

    /// <summary>Converts a stored UTC instant to the org's local wall-clock time, for display.</summary>
    public static DateTime ToLocal(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, _timeZone);
    }

    public static DateTime? ToLocal(DateTime? utc) => utc.HasValue ? ToLocal(utc.Value) : null;

    /// <summary>
    /// Treats <paramref name="local"/> as a naive org-local wall-clock value (e.g. from a date picker
    /// or a calendar-day boundary) and converts it to UTC for storage/querying.
    /// DateTime.MinValue/MaxValue are passed through unchanged since they're used as open-ended sentinels.
    /// </summary>
    public static DateTime ToUtc(DateTime local)
    {
        if (local == DateTime.MinValue || local == DateTime.MaxValue)
            return local;

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, _timeZone);
    }

    public static DateTime? ToUtc(DateTime? local) => local.HasValue ? ToUtc(local.Value) : null;

    /// <summary>Current instant expressed as org-local wall-clock time.</summary>
    public static DateTime NowLocal() => ToLocal(DateTime.UtcNow);

    /// <summary>Today's calendar date in the org's local timezone (not the server's).</summary>
    public static DateTime TodayLocalDate() => NowLocal().Date;

    /// <summary>The UTC instant range [start, end) covering today's calendar date in the org's timezone.</summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) TodayRangeUtc()
    {
        var today = TodayLocalDate();
        return (ToUtc(today), ToUtc(today.AddDays(1)));
    }
}
