using System.Globalization;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;

namespace Inprem.Platform.Application.Notifications;

public enum InboundOutcome
{
    /// <summary>Not a keyword: nothing recorded (texts are service alerts; replies are not read).</summary>
    Ignored,
    OptedOut,
    Resubscribed,
    Help,
}

/// <summary>
/// What Twilio tells us after a text (Phase 5 step 5.1; its signature is checked before this is
/// called): the delivery status of a message, and replies to Inprem's number. STOP and the other
/// opt-out words stop every text to that number at once; START lets the texts the person had
/// agreed to resume. Twilio's own Advanced Opt-Out answers the person; we only record it.
/// </summary>
public sealed class TwilioReports
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase) { "STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "REVOKE", "OPTOUT" };
    private static readonly HashSet<string> StartWords = new(StringComparer.OrdinalIgnoreCase) { "START", "UNSTOP", "YES" };
    private static readonly HashSet<string> HelpWords = new(StringComparer.OrdinalIgnoreCase) { "HELP", "INFO" };

    private readonly INotificationStore _store;
    private readonly IClock _clock;

    public TwilioReports(INotificationStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    /// <summary>A status callback. Returns false when the message is unknown or nothing changed.</summary>
    public async Task<bool> ApplyStatusAsync(string? messageSid, string? status, string? errorCode, string? price, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(messageSid) || string.IsNullOrWhiteSpace(status)) return false;
        var message = await _store.FindByExternalIdAsync(messageSid.Trim(), ct).ConfigureAwait(false);
        if (message == null) return false;

        var state = status.Trim().ToLowerInvariant() switch
        {
            "delivered" => DeliveryState.Delivered,
            "failed" or "undelivered" => DeliveryState.Failed,
            _ => DeliveryState.InProgress,
        };
        // Twilio gives the price as a negative amount in US dollars ("-0.00790"), often only in a later report.
        decimal? cost = decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? Math.Abs(amount) : null;
        if (!message.ApplyDeliveryReport(state, string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim(), cost, _clock.UtcNow)) return false;
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>A reply to Inprem's number.</summary>
    public async Task<InboundOutcome> InboundAsync(string? from, string? body, CancellationToken ct = default)
    {
        var phone = PhoneNumber.ToE164(from);
        var word = (body ?? "").Trim().TrimEnd('.', '!').Trim();
        if (phone == null || word.Length == 0) return InboundOutcome.Ignored;

        var now = _clock.UtcNow;
        if (StopWords.Contains(word))
        {
            await _store.AddConsentAsync(ConsentRecord.Record(phone, ConsentAction.Revoke, null, null, ConsentSources.SmsStop, null, now, "twilio"), ct).ConfigureAwait(false);
            await _store.AddAuditAsync(AuditEntry.Record("twilio", "notifications.opted-out", PhoneNumber.Mask(phone), "Replied STOP: no more texts to this number.", now), ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return InboundOutcome.OptedOut;
        }
        if (StartWords.Contains(word))
        {
            await _store.AddConsentAsync(ConsentRecord.Record(phone, ConsentAction.Resubscribe, null, null, ConsentSources.SmsStart, null, now, "twilio"), ct).ConfigureAwait(false);
            await _store.AddAuditAsync(AuditEntry.Record("twilio", "notifications.resubscribed", PhoneNumber.Mask(phone), "Replied START: the texts agreed to before resume.", now), ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return InboundOutcome.Resubscribed;
        }
        return HelpWords.Contains(word) ? InboundOutcome.Help : InboundOutcome.Ignored;
    }
}
