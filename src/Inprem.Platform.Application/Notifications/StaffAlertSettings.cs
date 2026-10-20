using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;
using Microsoft.Extensions.Caching.Memory;

namespace Inprem.Platform.Application.Notifications;

/// <summary>An organization's staff and volunteer alert settings, with the D32 defaults when never set.</summary>
public sealed record StaffAlertChoices(bool Enabled, TimeOnly MissedClockOut, int LongBreakMinutes, bool AdminSummary)
{
    public static readonly StaffAlertChoices Defaults = new(false, TenantMessagingSettings.DefaultMissedClockOut, TenantMessagingSettings.DefaultLongBreakMinutes, false);
}

/// <summary>
/// Staff and volunteer alerts for one organization (Phase 6 step 6.1, C3-8: off by default). The owner
/// sets them on the back office's settings page. Whether they are on decides whether the forms show the
/// mobile and consent fields (step 6.2), so the answer is kept for a minute; a change clears it at once.
/// </summary>
public sealed class StaffAlertSettings
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly INotificationStore _store;
    private readonly IMemoryCache _cache;
    private readonly NotificationOptions _options;
    private readonly IClock _clock;

    public StaffAlertSettings(INotificationStore store, IMemoryCache cache, NotificationOptions options, IClock clock)
    {
        _store = store;
        _cache = cache;
        _options = options;
        _clock = clock;
    }

    private static string Key(int tenantId) => "staff-alerts:" + tenantId;

    public async Task<StaffAlertChoices> GetAsync(int tenantId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(Key(tenantId), out StaffAlertChoices? cached) && cached != null) return cached;
        var settings = await _store.FindSettingsAsync(tenantId, ct).ConfigureAwait(false);
        var choices = settings == null
            ? StaffAlertChoices.Defaults
            : new StaffAlertChoices(settings.StaffAlertsEnabled, settings.MissedClockOutTime, settings.LongBreakMinutes, settings.AdminSummaryEnabled);
        _cache.Set(Key(tenantId), choices, CacheFor);
        return choices;
    }

    /// <summary>Saves the settings; null when saved, otherwise what is wrong.</summary>
    public async Task<string?> SaveAsync(int tenantId, StaffAlertChoices choices, string actor, CancellationToken ct = default)
    {
        if (choices.LongBreakMinutes is < TenantMessagingSettings.MinLongBreakMinutes or > TenantMessagingSettings.MaxLongBreakMinutes)
            return $"A long break is {TenantMessagingSettings.MinLongBreakMinutes} to {TenantMessagingSettings.MaxLongBreakMinutes} minutes.";
        var now = _clock.UtcNow;
        var settings = await _store.FindSettingsForUpdateAsync(tenantId, ct).ConfigureAwait(false);
        if (settings == null)
        {
            settings = TenantMessagingSettings.Create(tenantId, smsEnabled: true, _options.DefaultMonthlySmsLimit, now, actor);
            await _store.AddSettingsAsync(settings, ct).ConfigureAwait(false);
        }
        settings.SetStaffAlerts(choices.Enabled, choices.MissedClockOut, choices.LongBreakMinutes, choices.AdminSummary, now, actor);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "organization.staff-alerts-changed", $"tenant:{tenantId}",
            $"Staff alerts {(choices.Enabled ? "on" : "off")}; missed clock-out {choices.MissedClockOut:HH\\:mm}; long break {choices.LongBreakMinutes} min; admin summary {(choices.AdminSummary ? "on" : "off")}.", now, tenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        _cache.Remove(Key(tenantId));
        return null;
    }
}
