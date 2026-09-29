using InpremClockingApp.Data;

namespace InpremClockingApp.Services;

/// <summary>
/// Per-tenant replacement for the old static OrgClock (see multi-tenancy.md Phase 3). All
/// timestamps are stored in UTC; this converts to/from the current request's tenant's local
/// time at the edges - for display, and for interpreting locally-scoped values such as "today"
/// or an admin-entered clock time - so every part of the app agrees on what "now" and "today"
/// mean for that tenant.
/// </summary>
public interface ITenantClock
{
    TimeZoneInfo TimeZone { get; }

    /// <summary>Converts a stored UTC instant to the tenant's local wall-clock time, for display.</summary>
    DateTime ToLocal(DateTime utc);
    DateTime? ToLocal(DateTime? utc);

    /// <summary>
    /// Treats <paramref name="local"/> as a naive tenant-local wall-clock value (e.g. from a date
    /// picker or a calendar-day boundary) and converts it to UTC for storage/querying.
    /// </summary>
    DateTime ToUtc(DateTime local);
    DateTime? ToUtc(DateTime? local);

    /// <summary>Current instant expressed as the tenant's local wall-clock time.</summary>
    DateTime NowLocal();

    /// <summary>Today's calendar date in the tenant's local timezone (not the server's).</summary>
    DateTime TodayLocalDate();

    /// <summary>The UTC instant range [start, end) covering today's calendar date in the tenant's timezone.</summary>
    (DateTime StartUtc, DateTime EndUtcExclusive) TodayRangeUtc();
}

public class TenantClock : ITenantClock
{
    // Default for contexts with no resolved tenant (e.g. IdentitySeeder running at startup,
    // outside any request) - matches Inprem's own zone so existing behavior is unaffected there.
    private const string FallbackTimeZoneId = "America/New_York";

    private readonly ApplicationDbContext _db;
    private TimeZoneInfo? _timeZone;

    public TenantClock(ApplicationDbContext db)
    {
        _db = db;
    }

    // Resolved lazily (not in the constructor) and cached for the lifetime of this scoped
    // instance, since the tenant lookup needs the query filter - which needs the signed-in
    // user's claim to already be available, not always true at DI construction time - and
    // there's no reason to hit the database more than once per request for this.
    public TimeZoneInfo TimeZone => _timeZone ??= ResolveTimeZone();

    private TimeZoneInfo ResolveTimeZone()
    {
        // Uses the Tenant query filter from ApplicationDbContext, so this naturally resolves to
        // exactly the signed-in user's own tenant with no explicit Id needed.
        var timeZoneId = _db.Tenants.Select(t => t.TimeZoneId).FirstOrDefault() ?? FallbackTimeZoneId;
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public DateTime ToLocal(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, TimeZone);
    }

    public DateTime? ToLocal(DateTime? utc) => utc.HasValue ? ToLocal(utc.Value) : null;

    public DateTime ToUtc(DateTime local)
    {
        if (local == DateTime.MinValue || local == DateTime.MaxValue)
            return local;

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone);
    }

    public DateTime? ToUtc(DateTime? local) => local.HasValue ? ToUtc(local.Value) : null;

    public DateTime NowLocal() => ToLocal(DateTime.UtcNow);

    public DateTime TodayLocalDate() => NowLocal().Date;

    public (DateTime StartUtc, DateTime EndUtcExclusive) TodayRangeUtc()
    {
        var today = TodayLocalDate();
        return (ToUtc(today), ToUtc(today.AddDays(1)));
    }
}
