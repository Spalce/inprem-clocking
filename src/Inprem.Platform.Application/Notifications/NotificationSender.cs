using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;

namespace Inprem.Platform.Application.Notifications;

public sealed record SendReport(int Sent, int Failed, int Skipped, int Retrying);

/// <summary>
/// Sends the outbox's due messages (Phase 5 step 5.1; run by the send job): texts through
/// <see cref="ISmsSender"/>, emails through <see cref="IEmailSender"/>. Consent is checked again just
/// before a text goes, because the person may have replied STOP after it was queued. A temporary
/// failure is tried again later; a refusal because of STOP records the opt-out.
/// </summary>
public sealed class NotificationSender
{
    private readonly INotificationStore _store;
    private readonly ISmsSender _sms;
    private readonly IEmailSender _email;
    private readonly NotificationOptions _options;
    private readonly IClock _clock;

    public NotificationSender(INotificationStore store, ISmsSender sms, IEmailSender email, NotificationOptions options, IClock clock)
    {
        _store = store;
        _sms = sms;
        _email = email;
        _options = options;
        _clock = clock;
    }

    public async Task<SendReport> SendDueAsync(CancellationToken ct = default)
    {
        int sent = 0, failed = 0, skipped = 0, retrying = 0;
        foreach (var message in await _store.DueAsync(_clock.UtcNow, _options.BatchSize, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var now = _clock.UtcNow;
            if (message.Channel == NotificationChannel.Sms)
            {
                var consent = ConsentRecord.StateFor(await _store.ConsentHistoryAsync(message.Destination, ct).ConfigureAwait(false), message.TenantId);
                if (consent != ConsentState.Given)
                {
                    message.SkipQueued(consent == ConsentState.Withdrawn ? SkipReasons.OptedOut : SkipReasons.NoConsent, now);
                    await _store.SaveChangesAsync(ct).ConfigureAwait(false);
                    skipped++;
                    continue;
                }

                var result = await SendSmsAsync(message, ct).ConfigureAwait(false);
                if (result.Accepted)
                {
                    message.Sent(result.ExternalId, _clock.UtcNow);
                    sent++;
                }
                else if (result.RecipientOptedOut)
                {
                    // Twilio knows of a STOP we never heard about (e.g. a report that did not reach us).
                    message.SkipQueued(SkipReasons.OptedOut, now);
                    await _store.AddConsentAsync(ConsentRecord.Record(message.Destination, ConsentAction.Revoke, null, message.RecipientRef, ConsentSources.SmsStop, null, now, "twilio"), ct).ConfigureAwait(false);
                    await _store.AddAuditAsync(AuditEntry.Record("twilio", "notifications.opted-out", PhoneNumber.Mask(message.Destination), "Twilio refused a text: the number had replied STOP.", now, message.TenantId), ct).ConfigureAwait(false);
                    skipped++;
                }
                else
                {
                    message.AttemptFailed(result.Error ?? "The text was not accepted.", result.Temporary, TimeSpan.FromSeconds(_options.RetryDelaySeconds * Math.Max(1, message.Attempts + 1)), _options.MaxAttempts, now);
                    if (message.Status == NotificationStatus.Failed) failed++; else retrying++;
                }
            }
            else
            {
                try
                {
                    await _email.SendAsync(message.Destination, message.Subject ?? "Inprem Clock-in", NotificationTemplates.ToHtml(message.Body), ct).ConfigureAwait(false);
                    message.Sent(null, _clock.UtcNow);
                    sent++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Whether the mail server is down or refuses the address cannot be told apart reliably: retry, then give up.
                    message.AttemptFailed($"The email could not be sent ({e.GetType().Name}).", temporary: true, TimeSpan.FromSeconds(_options.RetryDelaySeconds * Math.Max(1, message.Attempts + 1)), _options.MaxAttempts, now);
                    if (message.Status == NotificationStatus.Failed) failed++; else retrying++;
                }
            }
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return new SendReport(sent, failed, skipped, retrying);
    }

    private async Task<SmsSendResult> SendSmsAsync(Notification message, CancellationToken ct)
    {
        try
        {
            return await _sms.SendAsync(message.Destination, message.Body, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return SmsSendResult.Fail($"The text could not be handed to the sender ({e.GetType().Name}).", temporary: true);
        }
    }
}
