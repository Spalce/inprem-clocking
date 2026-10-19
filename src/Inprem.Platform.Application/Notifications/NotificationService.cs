using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Time;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Notifications;

/// <summary>Who an event's message goes to.</summary>
public enum Audience
{
    /// <summary>The organization's active owners.</summary>
    OrganizationOwners,

    /// <summary>The organization's active owners and admins.</summary>
    OrganizationAdmins,

    /// <summary>Inprem's active console users (optionally only some roles).</summary>
    ProviderUsers,

    /// <summary>Exactly the recipients given (staff and volunteers in Phase 6; tests).</summary>
    Explicit,
}

/// <summary>
/// Something happened that someone should hear about (design 9: "Event"). <see cref="EventKey"/>
/// makes raising the same event again harmless; <see cref="Values"/> fill the template's placeholders;
/// <see cref="Urgency"/> overrides the message's own (e.g. a payment problem the provider wants at once).
/// </summary>
public sealed record NotificationEvent(
    string TemplateKey,
    int? TenantId,
    Audience Audience,
    IReadOnlyDictionary<string, string>? Values = null,
    string? EventKey = null,
    IReadOnlyCollection<ProviderRole>? ProviderRoles = null,
    IReadOnlyList<NotificationRecipient>? Recipients = null,
    IReadOnlyCollection<NotificationChannel>? Channels = null,
    NotificationUrgency? Urgency = null);

/// <summary>What raising an event did: the messages queued and the ones skipped, with their reasons.</summary>
public sealed record NotificationRaiseResult(int Queued, IReadOnlyList<(NotificationChannel Channel, string RecipientRef, string Reason)> Skipped)
{
    public static readonly NotificationRaiseResult Nothing = new(0, Array.Empty<(NotificationChannel, string, string)>());
}

/// <summary>
/// The notification module (Phase 5 step 5.1, design 9): picks the template and the recipients,
/// checks consent and opt-out, quiet hours in the organization's time zone, the organization's texts
/// switch and monthly limit and the platform's daily spend cap, and writes each message to the
/// outbox (Queued) or records why it was not sent (Skipped). Sending is the send job's
/// (<see cref="NotificationSender"/>). Emails need no consent (account messages) and ignore quiet
/// hours; texts need both.
/// </summary>
public sealed class NotificationService
{
    private readonly INotificationStore _store;
    private readonly IRecipientDirectory _directory;
    private readonly NotificationOptions _options;
    private readonly IClock _clock;

    public NotificationService(INotificationStore store, IRecipientDirectory directory, NotificationOptions options, IClock clock)
    {
        _store = store;
        _directory = directory;
        _options = options;
        _clock = clock;
    }

    public async Task<NotificationRaiseResult> RaiseAsync(NotificationEvent notificationEvent, CancellationToken ct = default)
    {
        var template = NotificationTemplates.Find(notificationEvent.TemplateKey)
            ?? throw new ArgumentException($"There is no message '{notificationEvent.TemplateKey}'.", nameof(notificationEvent));
        var now = _clock.UtcNow;

        OrganizationContact? organization = null;
        if (notificationEvent.TenantId is { } tenantId)
        {
            organization = await _directory.OrganizationAsync(tenantId, ct).ConfigureAwait(false);
            if (organization == null) return NotificationRaiseResult.Nothing; // the organization is gone
        }

        var recipients = notificationEvent.Audience switch
        {
            Audience.OrganizationOwners or Audience.OrganizationAdmins when organization != null =>
                await _directory.OrganizationMembersAsync(organization.TenantId, notificationEvent.Audience == Audience.OrganizationAdmins, ct).ConfigureAwait(false),
            Audience.ProviderUsers => await _directory.ProviderUsersAsync(notificationEvent.ProviderRoles ?? Array.Empty<ProviderRole>(), ct).ConfigureAwait(false),
            Audience.Explicit => notificationEvent.Recipients ?? Array.Empty<NotificationRecipient>(),
            _ => Array.Empty<NotificationRecipient>(),
        };
        if (recipients.Count == 0) return NotificationRaiseResult.Nothing;

        var zone = TimeZoneResolver.Resolve(organization?.TimeZoneId ?? _options.ProviderTimeZoneId);
        var channels = notificationEvent.Channels ?? new[] { NotificationChannel.Email, NotificationChannel.Sms };
        var urgency = notificationEvent.Urgency ?? template.Urgency;
        var limits = new SmsLimits(this, organization, zone, now);
        var queued = 0;
        var skipped = new List<(NotificationChannel, string, string)>();

        foreach (var channel in channels.Distinct())
        {
            var (subject, body, enabled) = await WordingAsync(template, channel, ct).ConfigureAwait(false);
            if (body == null) continue; // the message has no wording for this channel

            foreach (var recipient in recipients.DistinctBy(r => (r.Type, r.Ref)))
            {
                if (notificationEvent.EventKey != null && await _store.ExistsAsync(notificationEvent.EventKey, recipient.Ref, channel, ct).ConfigureAwait(false)) continue;

                var values = Values(notificationEvent, organization, recipient);
                var text = NotificationTemplates.Render(body, values);
                var title = subject == null ? null : NotificationTemplates.Render(subject, values);
                var destination = channel == NotificationChannel.Email ? recipient.Email?.Trim() : recipient.Phone;
                if (channel == NotificationChannel.Sms && string.IsNullOrWhiteSpace(destination)) continue; // no number given: texts are not for them

                var reason = !enabled ? SkipReasons.TemplateOff
                    : string.IsNullOrWhiteSpace(destination) ? SkipReasons.NoAddress
                    : channel == NotificationChannel.Sms ? await limits.SmsRefusalAsync(destination, notificationEvent.TenantId, ct).ConfigureAwait(false)
                    : null;

                if (reason != null)
                {
                    await _store.AddAsync(Notification.Skip(notificationEvent.TenantId, recipient.Type, recipient.Ref, channel,
                        channel == NotificationChannel.Sms ? PhoneNumber.ToE164(destination) ?? "" : destination,
                        template.Key, title, text, urgency, notificationEvent.EventKey, reason, now), ct).ConfigureAwait(false);
                    skipped.Add((channel, recipient.Ref, reason));
                    continue;
                }

                var to = channel == NotificationChannel.Sms ? PhoneNumber.ToE164(destination)! : destination!;
                var notBefore = channel == NotificationChannel.Sms && urgency == NotificationUrgency.Normal
                    ? QuietHours.NextAllowedUtc(now, zone, _options.QuietStart, _options.QuietEnd)
                    : now;
                await _store.AddAsync(Notification.Queue(notificationEvent.TenantId, recipient.Type, recipient.Ref, channel, to,
                    template.Key, title, text, urgency, notificationEvent.EventKey, _options.EstimatedSmsCostUsd, now, notBefore), ct).ConfigureAwait(false);
                if (channel == NotificationChannel.Sms) limits.Counted();
                queued++;
            }
        }

        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return new NotificationRaiseResult(queued, skipped);
    }

    /// <summary>
    /// Records that the person agreed to texts (website signup's box, a console change), with the
    /// exact wording. A number that is not a full international number is not recorded (returns false).
    /// </summary>
    public async Task<bool> RecordConsentAsync(string? phone, int? tenantId, string? personRef, string source, string wording, string actor, CancellationToken ct = default)
    {
        var e164 = PhoneNumber.ToE164(phone);
        if (e164 == null) return false;
        var now = _clock.UtcNow;
        await _store.AddConsentAsync(ConsentRecord.Record(e164, ConsentAction.Grant, tenantId, personRef, source, wording, now, actor), ct).ConfigureAwait(false);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "notifications.consent-granted", PhoneNumber.Mask(e164), $"Texts agreed to ({source}).", now, tenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<(string? Subject, string? Body, bool Enabled)> WordingAsync(TemplateDefault template, NotificationChannel channel, CancellationToken ct)
    {
        var fallback = channel == NotificationChannel.Email ? template.EmailBody : template.SmsBody;
        if (fallback == null) return (null, null, false);
        var edited = await _store.FindTemplateAsync(template.Key, channel, ct).ConfigureAwait(false);
        return edited != null
            ? (edited.Subject, edited.Body, edited.Enabled)
            : (channel == NotificationChannel.Email ? template.EmailSubject : null, fallback, true);
    }

    private static Dictionary<string, string> Values(NotificationEvent notificationEvent, OrganizationContact? organization, NotificationRecipient recipient)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["firstName"] = string.IsNullOrWhiteSpace(recipient.FirstName) ? "there" : recipient.FirstName.Trim(),
            ["organization"] = organization?.Name ?? "Inprem",
        };
        foreach (var (key, value) in notificationEvent.Values ?? new Dictionary<string, string>()) values[key] = value;
        return values;
    }

    /// <summary>The checks a text must pass, counting the texts queued in this same event.</summary>
    private sealed class SmsLimits
    {
        private readonly NotificationService _service;
        private readonly OrganizationContact? _organization;
        private readonly TimeZoneInfo _zone;
        private readonly DateTime _now;
        private TenantMessagingSettings? _settings;
        private bool _settingsLoaded;
        private int? _monthCount;
        private decimal? _daySpend;
        private int _added;

        public SmsLimits(NotificationService service, OrganizationContact? organization, TimeZoneInfo zone, DateTime now)
        {
            _service = service;
            _organization = organization;
            _zone = zone;
            _now = now;
        }

        public void Counted() => _added++;

        public async Task<string?> SmsRefusalAsync(string phone, int? tenantId, CancellationToken ct)
        {
            var e164 = PhoneNumber.ToE164(phone);
            if (e164 == null) return SkipReasons.InvalidPhone;

            var consent = ConsentRecord.StateFor(await _service._store.ConsentHistoryAsync(e164, ct).ConfigureAwait(false), tenantId);
            if (consent == ConsentState.Withdrawn) return SkipReasons.OptedOut;
            if (consent == ConsentState.NotGiven) return SkipReasons.NoConsent;

            if (_organization != null)
            {
                if (!_settingsLoaded)
                {
                    _settings = await _service._store.FindSettingsAsync(_organization.TenantId, ct).ConfigureAwait(false);
                    _settingsLoaded = true;
                }
                if (_settings is { SmsEnabled: false }) return SkipReasons.SmsOff;
                var limit = _settings?.MonthlySmsLimit ?? _service._options.DefaultMonthlySmsLimit;
                _monthCount ??= await _service._store.CountSmsAsync(_organization.TenantId, QuietHours.MonthStartUtc(_now, _zone), ct).ConfigureAwait(false);
                if (_monthCount + _added >= limit) return SkipReasons.MonthlyLimit;
            }

            _daySpend ??= await _service._store.SmsSpendAsync(_now.Date, ct).ConfigureAwait(false);
            if (_daySpend + (_added + 1) * _service._options.EstimatedSmsCostUsd > _service._options.DailySpendCapUsd) return SkipReasons.DailySpendCap;
            return null;
        }
    }
}
