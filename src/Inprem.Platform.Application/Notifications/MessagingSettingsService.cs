using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;
using Microsoft.Extensions.Caching.Memory;

namespace Inprem.Platform.Application.Notifications;

/// <summary>
/// An organization's message settings that the console changes (Phase 5). Step 5.2: whether its
/// admins enter a texted code after their password. Read on every app request of the organization,
/// so the answer is kept for a minute; a change clears it at once on this server.
/// </summary>
public sealed class MessagingSettingsService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly INotificationStore _store;
    private readonly IMemoryCache _cache;
    private readonly NotificationOptions _options;
    private readonly IClock _clock;

    public MessagingSettingsService(INotificationStore store, IMemoryCache cache, NotificationOptions options, IClock clock)
    {
        _store = store;
        _cache = cache;
        _options = options;
        _clock = clock;
    }

    private static string Key(int tenantId) => "sms-sign-in:" + tenantId;

    public async Task<bool> SmsSignInRequiredAsync(int tenantId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(Key(tenantId), out bool required)) return required;
        required = (await _store.FindSettingsAsync(tenantId, ct).ConfigureAwait(false))?.SmsSignInRequired ?? false;
        _cache.Set(Key(tenantId), required, CacheFor);
        return required;
    }

    /// <summary>Switches the admins' texted sign-in code on or off. Returns false when it was already so.</summary>
    public async Task<bool> SetSmsSignInAsync(int tenantId, bool required, string actor, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var settings = await _store.FindSettingsForUpdateAsync(tenantId, ct).ConfigureAwait(false);
        if (settings == null)
        {
            settings = TenantMessagingSettings.Create(tenantId, smsEnabled: true, _options.DefaultMonthlySmsLimit, now, actor);
            await _store.AddSettingsAsync(settings, ct).ConfigureAwait(false);
        }
        if (!settings.RequireSmsSignIn(required, now, actor)) return false;

        await _store.AddAuditAsync(AuditEntry.Record(actor, required ? "organization.sms-sign-in-on" : "organization.sms-sign-in-off", $"tenant:{tenantId}",
            required ? "Admins enter a texted code after their password." : "Admins sign in with their password only.", now, tenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        _cache.Remove(Key(tenantId));
        return true;
    }
}
