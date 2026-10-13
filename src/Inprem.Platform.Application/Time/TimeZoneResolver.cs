namespace Inprem.Platform.Application.Time;

/// <summary>
/// Finds a time zone from the IANA id stored on the organization (e.g. America/New_York), also
/// on hosts whose .NET cannot read IANA ids directly (Windows without ICU): then the id is
/// converted to its Windows equivalent first. Unknown ids fall back to the given default.
/// </summary>
public static class TimeZoneResolver
{
    public const string DefaultTimeZoneId = "America/New_York";

    public static TimeZoneInfo Resolve(string? timeZoneId, string fallbackId = DefaultTimeZoneId) =>
        TryFind(timeZoneId) ?? TryFind(fallbackId) ?? TimeZoneInfo.Utc;

    public static TimeZoneInfo? TryFind(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return null;
    }
}
