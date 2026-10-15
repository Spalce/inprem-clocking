namespace Inprem.Platform.Domain.Billing;

public enum BillingEventStatus
{
    Received,
    Processed,
    Failed,
}

/// <summary>
/// A verified PayPal webhook event, stored once by PayPal's event id (Phase 2 step 2.1, design 8).
/// The raw body is kept for replay; it contains payer details, so it is never logged.
/// </summary>
public sealed class BillingEvent
{
    public const int MaxErrorLength = 2000;

    private BillingEvent()
    {
        ExternalEventId = "";
        EventType = "";
        RawJson = "";
    }

    /// <summary>PayPal's event id (primary key: a duplicate delivery is a no-op).</summary>
    public string ExternalEventId { get; private set; }
    public string EventType { get; private set; }
    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public string RawJson { get; private set; }
    public DateTime ReceivedUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }

    public BillingEventStatus Status => ProcessedUtc != null ? BillingEventStatus.Processed : Error != null ? BillingEventStatus.Failed : BillingEventStatus.Received;

    public static BillingEvent Receive(string externalEventId, string eventType, string? resourceType, string? resourceId, string rawJson, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(externalEventId) || externalEventId.Length > 100) throw new ArgumentException("PayPal event id required (at most 100 characters).", nameof(externalEventId));
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 100) throw new ArgumentException("Event type required (at most 100 characters).", nameof(eventType));
        if (string.IsNullOrWhiteSpace(rawJson)) throw new ArgumentException("The event body is required.", nameof(rawJson));
        return new BillingEvent
        {
            ExternalEventId = externalEventId, EventType = eventType, ResourceType = Cut(resourceType, 50), ResourceId = Cut(resourceId, 100),
            RawJson = rawJson, ReceivedUtc = nowUtc,
        };
    }

    /// <summary>A processing attempt starts (also a console replay).</summary>
    public void StartAttempt()
    {
        Attempts++;
        ProcessedUtc = null;
    }

    public void Processed(DateTime nowUtc)
    {
        ProcessedUtc = nowUtc;
        Error = null;
    }

    public void Failed(string error)
    {
        ProcessedUtc = null;
        Error = Cut(string.IsNullOrWhiteSpace(error) ? "Unknown error." : error, MaxErrorLength);
    }

    private static string? Cut(string? value, int max) => value == null ? null : value.Length > max ? value[..max] : value;
}
