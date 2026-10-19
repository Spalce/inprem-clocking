namespace Inprem.Platform.Application.Notifications;

/// <summary>
/// Quiet hours in a time zone (design 9): no normal text between the start and the end, local time.
/// The end may be on the next day (21:00 to 08:00), as it usually is.
/// </summary>
public static class QuietHours
{
    /// <summary>The first moment at or after <paramref name="nowUtc"/> outside quiet hours, in UTC.</summary>
    public static DateTime NextAllowedUtc(DateTime nowUtc, TimeZoneInfo zone, TimeOnly start, TimeOnly end)
    {
        if (start == end) return nowUtc; // no quiet hours
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var time = TimeOnly.FromDateTime(local);
        var overnight = start > end;
        var quiet = overnight ? time >= start || time < end : time >= start && time < end;
        if (!quiet) return nowUtc;

        var endDay = DateOnly.FromDateTime(local);
        if (overnight && time >= start) endDay = endDay.AddDays(1);
        return ToUtc(endDay.ToDateTime(end), zone);
    }

    /// <summary>A local time as UTC; a time skipped by a clock change (spring) moves forward by the gap.</summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    /// <summary>The start of the calendar month containing <paramref name="nowUtc"/> in the time zone, in UTC.</summary>
    public static DateTime MonthStartUtc(DateTime nowUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        return ToUtc(new DateTime(local.Year, local.Month, 1), zone);
    }
}
