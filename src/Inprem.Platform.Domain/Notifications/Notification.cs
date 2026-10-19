namespace Inprem.Platform.Domain.Notifications;

public enum NotificationChannel
{
    Email,
    Sms,
}

public enum NotificationStatus
{
    /// <summary>Waiting to be sent (at once, or after quiet hours: <see cref="Notification.NotBeforeUtc"/>).</summary>
    Queued,

    /// <summary>Handed to the sender (Twilio accepted it; an email went out).</summary>
    Sent,

    /// <summary>Twilio reported it delivered to the phone.</summary>
    Delivered,

    /// <summary>Could not be sent or delivered (the reason is in <see cref="Notification.Error"/>).</summary>
    Failed,

    /// <summary>Never sent, on purpose (the reason is in <see cref="Notification.SkipReason"/>).</summary>
    Skipped,
}

public enum RecipientType
{
    OrganizationOwner,
    OrganizationAdmin,
    ProviderUser,

    /// <summary>A staff member or volunteer (Phase 6).</summary>
    Person,
}

public enum NotificationUrgency
{
    /// <summary>Waits for the end of quiet hours (08:00 in the organization's time zone).</summary>
    Normal,

    /// <summary>Sent at once, also at night (system problems).</summary>
    Urgent,
}

/// <summary>Why a message was never sent (design 9: "Skipped (reason logged)").</summary>
public static class SkipReasons
{
    public const string NoAddress = "no-address";
    public const string InvalidPhone = "invalid-phone";
    public const string NoConsent = "no-consent";
    public const string OptedOut = "opted-out";
    public const string SmsOff = "sms-off";
    public const string MonthlyLimit = "monthly-limit";
    public const string DailySpendCap = "daily-spend-cap";
    public const string TemplateOff = "template-off";
}

/// <summary>
/// One message in the outbox (Phase 5 step 5.1, design 9). Written by the notification service, with
/// status Queued or Skipped; the send job sends it; Twilio's status reports finish it. The rendered
/// text is kept (what was sent), the destination too: both are personal data, so neither is logged.
/// </summary>
public sealed class Notification
{
    public const int MaxBodyLength = 4000;
    public const int MaxErrorLength = 500;

    private Notification()
    {
        RecipientRef = "";
        Destination = "";
        TemplateKey = "";
        Body = "";
    }

    public Guid Id { get; private set; }

    /// <summary>The organization it is about; null for platform messages (to Inprem).</summary>
    public int? TenantId { get; private set; }
    public RecipientType RecipientType { get; private set; }

    /// <summary>Who it is for: the account id (AspNetUsers.Id), or the person's id for staff and volunteers.</summary>
    public string RecipientRef { get; private set; }
    public NotificationChannel Channel { get; private set; }

    /// <summary>The phone number (E.164) or email address; empty when there was none (Skipped).</summary>
    public string Destination { get; private set; }
    public string TemplateKey { get; private set; }

    /// <summary>For an email; null for a text.</summary>
    public string? Subject { get; private set; }

    /// <summary>The rendered text (the "payload"): exactly what is sent.</summary>
    public string Body { get; private set; }
    public NotificationUrgency Urgency { get; private set; }

    /// <summary>
    /// The event this message belongs to, e.g. "trial-ending-7:42:2026-11-01". With the recipient and
    /// channel it is unique, so raising the same event twice sends nothing twice.
    /// </summary>
    public string? EventKey { get; private set; }

    public NotificationStatus Status { get; private set; }
    public string? SkipReason { get; private set; }

    /// <summary>Twilio's message id (SM…), or the stand-in's.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>What Twilio charged, in US dollars (from its status report); null until known.</summary>
    public decimal? Cost { get; private set; }

    /// <summary>What the message is counted at for the daily spend cap until Twilio reports the real cost.</summary>
    public decimal EstimatedCost { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    /// <summary>Not sent before this time (the end of quiet hours); the creation time otherwise.</summary>
    public DateTime NotBeforeUtc { get; private set; }
    public DateTime? SentUtc { get; private set; }
    public DateTime? FinishedUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }

    public static Notification Queue(
        int? tenantId, RecipientType recipientType, string recipientRef, NotificationChannel channel, string destination,
        string templateKey, string? subject, string body, NotificationUrgency urgency, string? eventKey,
        decimal estimatedCost, DateTime nowUtc, DateTime notBeforeUtc)
    {
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("A queued message needs a destination.", nameof(destination));
        if (channel == NotificationChannel.Sms && PhoneNumber.ToE164(destination) != destination) throw new ArgumentException("Texts go to E.164 numbers.", nameof(destination));
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength) throw new ArgumentException($"A text of 1 to {MaxBodyLength} characters.", nameof(body));
        if (notBeforeUtc < nowUtc) notBeforeUtc = nowUtc;
        var notification = New(tenantId, recipientType, recipientRef, channel, destination, templateKey, subject, body, urgency, eventKey, nowUtc);
        notification.Status = NotificationStatus.Queued;
        notification.EstimatedCost = channel == NotificationChannel.Sms ? estimatedCost : 0;
        notification.NotBeforeUtc = notBeforeUtc;
        return notification;
    }

    public static Notification Skip(
        int? tenantId, RecipientType recipientType, string recipientRef, NotificationChannel channel, string? destination,
        string templateKey, string? subject, string body, NotificationUrgency urgency, string? eventKey, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 50) throw new ArgumentException("A skip reason (at most 50 characters).", nameof(reason));
        var notification = New(tenantId, recipientType, recipientRef, channel, destination ?? "", templateKey, subject, Cut(body, MaxBodyLength) ?? "", urgency, eventKey, nowUtc);
        notification.Status = NotificationStatus.Skipped;
        notification.SkipReason = reason;
        notification.NotBeforeUtc = nowUtc;
        notification.FinishedUtc = nowUtc;
        return notification;
    }

    private static Notification New(
        int? tenantId, RecipientType recipientType, string recipientRef, NotificationChannel channel, string destination,
        string templateKey, string? subject, string body, NotificationUrgency urgency, string? eventKey, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(recipientRef) || recipientRef.Length > 450) throw new ArgumentException("The recipient (at most 450 characters).", nameof(recipientRef));
        if (string.IsNullOrWhiteSpace(templateKey) || templateKey.Length > 100) throw new ArgumentException("The template key (at most 100 characters).", nameof(templateKey));
        if (eventKey?.Length > 200) throw new ArgumentException("At most 200 characters.", nameof(eventKey));
        if (destination.Length > 256) throw new ArgumentException("At most 256 characters.", nameof(destination));
        return new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RecipientType = recipientType, RecipientRef = recipientRef, Channel = channel,
            Destination = destination, TemplateKey = templateKey, Subject = channel == NotificationChannel.Email ? Cut(subject, 200) : null,
            Body = body, Urgency = urgency, EventKey = string.IsNullOrWhiteSpace(eventKey) ? null : eventKey, CreatedUtc = nowUtc,
        };
    }

    /// <summary>The sender accepted it (Twilio's id, or the stand-in's).</summary>
    public void Sent(string? externalId, DateTime nowUtc)
    {
        if (Status != NotificationStatus.Queued) throw new InvalidOperationException($"Only a queued message is sent (this one is {Status}).");
        Attempts++;
        Status = NotificationStatus.Sent;
        ExternalId = Cut(externalId, 64);
        SentUtc = nowUtc;
        Error = null;
    }

    /// <summary>
    /// A failed attempt. A temporary problem (Twilio down, a time-out) stays Queued and waits
    /// <paramref name="retryAfter"/>; after <paramref name="maxAttempts"/> attempts, or a permanent
    /// problem (a wrong number), it is Failed.
    /// </summary>
    public void AttemptFailed(string error, bool temporary, TimeSpan retryAfter, int maxAttempts, DateTime nowUtc)
    {
        if (Status != NotificationStatus.Queued) throw new InvalidOperationException($"Only a queued message is sent (this one is {Status}).");
        Attempts++;
        Error = Cut(string.IsNullOrWhiteSpace(error) ? "Unknown error." : error, MaxErrorLength);
        if (temporary && Attempts < maxAttempts)
        {
            NotBeforeUtc = nowUtc + retryAfter;
            return;
        }
        Status = NotificationStatus.Failed;
        FinishedUtc = nowUtc;
    }

    /// <summary>Not sent after all, decided at send time (e.g. the person replied STOP after it was queued).</summary>
    public void SkipQueued(string reason, DateTime nowUtc)
    {
        if (Status != NotificationStatus.Queued) throw new InvalidOperationException($"Only a queued message is skipped (this one is {Status}).");
        Status = NotificationStatus.Skipped;
        SkipReason = reason;
        FinishedUtc = nowUtc;
    }

    /// <summary>
    /// Twilio's status report. Reports can arrive late or out of order, so a finished message is not
    /// reopened ("sent" after "delivered" changes nothing); the cost is kept whenever it comes.
    /// Returns false when nothing changed.
    /// </summary>
    public bool ApplyDeliveryReport(DeliveryState state, string? errorCode, decimal? cost, DateTime nowUtc)
    {
        var changed = false;
        if (cost is { } price && Cost != price)
        {
            Cost = price;
            changed = true;
        }
        if (Status is NotificationStatus.Skipped or NotificationStatus.Queued) return changed;

        var next = state switch
        {
            DeliveryState.Delivered => NotificationStatus.Delivered,
            DeliveryState.Failed => NotificationStatus.Failed,
            _ => NotificationStatus.Sent,
        };
        if (next == Status || Status is NotificationStatus.Delivered or NotificationStatus.Failed) return changed;
        Status = next;
        if (next == NotificationStatus.Failed) Error = Cut($"Not delivered (Twilio error {errorCode ?? "unknown"}).", MaxErrorLength);
        if (next != NotificationStatus.Sent) FinishedUtc = nowUtc;
        return true;
    }

    private static string? Cut(string? value, int max) => value == null ? null : value.Length > max ? value[..max] : value;
}

/// <summary>Twilio's message states, reduced to what the outbox records.</summary>
public enum DeliveryState
{
    /// <summary>queued, accepted, sending, sent: on its way.</summary>
    InProgress,
    Delivered,

    /// <summary>failed, undelivered.</summary>
    Failed,
}
