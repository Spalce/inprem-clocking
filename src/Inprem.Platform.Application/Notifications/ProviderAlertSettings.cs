using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;

namespace Inprem.Platform.Application.Notifications;

/// <summary>What a console user sees on "My alerts".</summary>
public sealed record ProviderAlertChoices(bool NewSignups, AlertDelivery BillingProblems, bool SystemProblems, bool Texts);

/// <summary>
/// A console user's alert choices (Phase 5 step 5.3): which alerts, billing problems at once or in a
/// daily summary, and whether to get them by text too (a consent for Inprem's texts, with its wording,
/// on the phone they confirmed for codes). Switching texts off withdraws only Inprem's texts.
/// </summary>
public sealed class ProviderAlertSettings
{
    private readonly INotificationStore _store;
    private readonly IClock _clock;

    public ProviderAlertSettings(INotificationStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<ProviderAlertChoices> GetAsync(string userId, string? confirmedPhone, CancellationToken ct = default)
    {
        var chosen = (await _store.PreferencesAsync(new[] { userId }, ct).ConfigureAwait(false)).GetValueOrDefault(userId) ?? ProviderAlertPreferences.Defaults(userId);
        var phone = PhoneNumber.ToE164(confirmedPhone);
        var texts = phone != null && ConsentRecord.StateFor(await _store.ConsentHistoryAsync(phone, ct).ConfigureAwait(false), null) == ConsentState.Given;
        return new ProviderAlertChoices(chosen.NewSignups, chosen.BillingProblems, chosen.SystemProblems, texts);
    }

    /// <summary>Saves the choices; texts can only be switched on with a confirmed phone (returns false otherwise).</summary>
    public async Task<bool> SaveAsync(string userId, string? confirmedPhone, ProviderAlertChoices choices, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var phone = PhoneNumber.ToE164(confirmedPhone);
        if (choices.Texts && phone == null) return false;

        var preferences = await _store.FindPreferencesForUpdateAsync(userId, ct).ConfigureAwait(false);
        if (preferences == null)
        {
            preferences = ProviderAlertPreferences.Defaults(userId);
            await _store.AddPreferencesAsync(preferences, ct).ConfigureAwait(false);
        }
        preferences.Change(choices.NewSignups, choices.BillingProblems, choices.SystemProblems, now);

        if (phone != null)
        {
            var state = ConsentRecord.StateFor(await _store.ConsentHistoryAsync(phone, ct).ConfigureAwait(false), null);
            if (choices.Texts && state != ConsentState.Given)
                await _store.AddConsentAsync(ConsentRecord.Record(phone, ConsentAction.Grant, null, userId, ConsentSources.Console, ConsentWording.ProviderAlerts, now, $"user:{userId}"), ct).ConfigureAwait(false);
            if (!choices.Texts && state == ConsentState.Given)
                await _store.AddConsentAsync(ConsentRecord.Record(phone, ConsentAction.Revoke, null, userId, ConsentSources.Console, null, now, $"user:{userId}"), ct).ConfigureAwait(false);
        }

        await _store.AddAuditAsync(AuditEntry.Record($"user:{userId}", "provider.alerts-changed", userId,
            $"New signups {(choices.NewSignups ? "on" : "off")}; billing problems {choices.BillingProblems}; system problems {(choices.SystemProblems ? "on" : "off")}; texts {(choices.Texts ? "on" : "off")}.", now), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
