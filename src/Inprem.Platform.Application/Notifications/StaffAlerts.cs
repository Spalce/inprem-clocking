using System.Globalization;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Time;
using Inprem.Platform.Domain.Notifications;

namespace Inprem.Platform.Application.Notifications;

/// <summary>An organization that switched staff alerts on, with its choices and its time zone.</summary>
public sealed record StaffAlertOrganization(int TenantId, string TimeZoneId, StaffAlertChoices Choices);

/// <summary>
/// A staff member's or volunteer's session of one day that is not clocked out yet. Times are UTC;
/// <see cref="BreakStartUtc"/> is set while the person is on a break (left, not yet back).
/// </summary>
public sealed record OpenClocking(string PersonRef, string? FirstName, string? FullName, string? Phone, DateTime ClockInUtc, DateTime? BreakStartUtc);

/// <summary>Where the staff alerts check reads from: the organizations with alerts on, and the sessions not clocked out.</summary>
public interface IStaffAlertSource
{
    Task<IReadOnlyList<StaffAlertOrganization>> OrganizationsWithAlertsAsync(CancellationToken ct = default);

    /// <summary>The organization's sessions of the local day <paramref name="clockDate"/> that are clocked in and not out.</summary>
    Task<IReadOnlyList<OpenClocking>> OpenClockingsAsync(int tenantId, DateOnly clockDate, CancellationToken ct = default);
}

/// <summary>What one run of the check raised.</summary>
public sealed record StaffAlertsReport(int Organizations, int MissedClockOuts, int LongBreaks, int Summaries)
{
    public int Raised => MissedClockOuts + LongBreaks + Summaries;
}

/// <summary>
/// The staff and volunteer alerts (Phase 6 step 6.3, design Appendix C), run every few minutes by the job.
/// For each organization with staff alerts on, in its own time zone (organizations are grouped by zone):
/// <list type="bullet">
/// <item>Missed clock-out: from the set time of the local day, everyone who clocked in before it and is
/// still clocked in gets a text.</item>
/// <item>Long break: whoever has been on a break longer than the set minutes gets a text.</item>
/// <item>End-of-day summary (when chosen): from the set time, the admins get one email listing who is
/// still clocked in (nothing when nobody is).</item>
/// </list>
/// Texts go through the notification module: only with the person's recorded consent, never after STOP,
/// within the organization's limits. A staff text that falls in quiet hours is dropped, not held for the
/// morning (developer's decision, 2026-10-20). Each alert has an event key for the person and the day,
/// so later runs send nothing twice; one organization's failure does not stop the others.
/// </summary>
public sealed class StaffAlertsCheck
{
    private readonly IStaffAlertSource _source;
    private readonly NotificationService _notifications;
    private readonly INotificationStore _store;
    private readonly IClock _clock;

    public StaffAlertsCheck(IStaffAlertSource source, NotificationService notifications, INotificationStore store, IClock clock)
    {
        _source = source;
        _notifications = notifications;
        _store = store;
        _clock = clock;
    }

    public async Task<StaffAlertsReport> RunAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var organizations = await _source.OrganizationsWithAlertsAsync(ct).ConfigureAwait(false);
        int missed = 0, breaks = 0, summaries = 0;

        foreach (var zoneGroup in organizations.GroupBy(o => o.TimeZoneId))
        {
            var zone = TimeZoneResolver.Resolve(zoneGroup.Key);
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone);
            var day = DateOnly.FromDateTime(local);

            foreach (var organization in zoneGroup)
            {
                try
                {
                    var (m, b, s) = await OrganizationAsync(organization, zone, day, now, ct).ConfigureAwait(false);
                    missed += m;
                    breaks += b;
                    summaries += s;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _store.DiscardPending(); // the next run tries this organization again; the others go on
                }
            }
        }
        return new StaffAlertsReport(organizations.Count, missed, breaks, summaries);
    }

    private async Task<(int Missed, int Breaks, int Summaries)> OrganizationAsync(StaffAlertOrganization organization, TimeZoneInfo zone, DateOnly day, DateTime now, CancellationToken ct)
    {
        var open = await _source.OpenClockingsAsync(organization.TenantId, day, ct).ConfigureAwait(false);
        if (open.Count == 0) return (0, 0, 0);

        var choices = organization.Choices;
        var missedAtUtc = LocalToUtc(day.ToDateTime(choices.MissedClockOut), zone);
        var stillIn = now >= missedAtUtc ? open.Where(c => c.ClockInUtc < missedAtUtc).ToList() : new List<OpenClocking>();
        var longBreak = TimeSpan.FromMinutes(choices.LongBreakMinutes);
        int missed = 0, breaks = 0, summaries = 0;
        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var person in stillIn)
        {
            missed += (await _notifications.RaiseAsync(PersonEvent(NotificationTemplates.MissedClockOut, organization.TenantId, person,
                new Dictionary<string, string> { ["clockIn"] = Time(person.ClockInUtc, zone) },
                $"staff-missed-clock-out:{organization.TenantId}:{person.PersonRef}:{date}"), ct).ConfigureAwait(false)).Queued;
        }

        foreach (var person in open.Where(c => c.BreakStartUtc is { } started && now - started >= longBreak))
        {
            breaks += (await _notifications.RaiseAsync(PersonEvent(NotificationTemplates.LongBreak, organization.TenantId, person,
                new Dictionary<string, string> { ["breakStart"] = Time(person.BreakStartUtc!.Value, zone) },
                $"staff-long-break:{organization.TenantId}:{person.PersonRef}:{date}"), ct).ConfigureAwait(false)).Queued;
        }

        if (choices.AdminSummary && stillIn.Count > 0)
        {
            var lines = stillIn.OrderBy(c => c.ClockInUtc)
                .Select(c => $"- {(string.IsNullOrWhiteSpace(c.FullName) ? c.FirstName : c.FullName.Trim())}, since {Time(c.ClockInUtc, zone)}{(c.BreakStartUtc is { } b ? $" (on a break since {Time(b, zone)})" : "")}");
            summaries += (await _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.ShiftSummary, organization.TenantId, Audience.OrganizationAdmins,
                new Dictionary<string, string>
                {
                    ["count"] = stillIn.Count.ToString(CultureInfo.InvariantCulture),
                    ["time"] = choices.MissedClockOut.ToString("HH:mm", CultureInfo.InvariantCulture),
                    ["date"] = day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
                    ["lines"] = string.Join("\n", lines),
                },
                EventKey: $"staff-shift-summary:{organization.TenantId}:{date}", Channels: new[] { NotificationChannel.Email }), ct).ConfigureAwait(false)).Queued > 0 ? 1 : 0;
        }
        return (missed, breaks, summaries);
    }

    private static NotificationEvent PersonEvent(string template, int tenantId, OpenClocking person, Dictionary<string, string> values, string eventKey) =>
        new(template, tenantId, Audience.Explicit, values, eventKey,
            Recipients: new[] { new NotificationRecipient(RecipientType.Person, person.PersonRef, null, person.Phone, person.FirstName) },
            Channels: new[] { NotificationChannel.Sms }, DropInQuietHours: true);

    private static string Time(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A local time as UTC; a time skipped by a clock change (spring) moves forward by the gap; a repeated one (autumn) is the first.</summary>
    private static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(30);
        if (zone.IsAmbiguousTime(unspecified))
            return DateTime.SpecifyKind(unspecified - zone.GetAmbiguousTimeOffsets(unspecified).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }
}
