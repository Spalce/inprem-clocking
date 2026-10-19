namespace Inprem.Platform.Domain.Notifications;

/// <summary>
/// An edited wording of a message (Phase 5, design 9). The default wording of every message is in the
/// code (the template catalogue); a row here replaces it for one key and channel, set in the console.
/// Placeholders are written {name}. A template can also be switched off: nothing is then sent for it
/// (Skipped, "template-off").
/// </summary>
public sealed class NotificationTemplate
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 2000;

    private NotificationTemplate()
    {
        Key = "";
        Body = "";
        UpdatedBy = "";
    }

    public string Key { get; private set; }
    public NotificationChannel Channel { get; private set; }

    /// <summary>Emails only.</summary>
    public string? Subject { get; private set; }
    public string Body { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public string UpdatedBy { get; private set; }

    public static NotificationTemplate Create(string key, NotificationChannel channel, string? subject, string body, bool enabled, DateTime nowUtc, string updatedBy)
    {
        var template = new NotificationTemplate { Key = key, Channel = channel };
        template.Change(subject, body, enabled, nowUtc, updatedBy);
        return template;
    }

    public void Change(string? subject, string body, bool enabled, DateTime nowUtc, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(Key) || Key.Length > 100) throw new ArgumentException("The template key (at most 100 characters).");
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        var text = (body ?? "").Trim();
        if (text.Length is 0 or > MaxBodyLength) throw new ArgumentException($"A text of 1 to {MaxBodyLength} characters.", nameof(body));
        var title = subject?.Trim();
        if (Channel == NotificationChannel.Email && string.IsNullOrEmpty(title)) throw new ArgumentException("An email needs a subject.", nameof(subject));
        if (title?.Length > MaxSubjectLength) throw new ArgumentException($"At most {MaxSubjectLength} characters.", nameof(subject));
        Subject = Channel == NotificationChannel.Email ? title : null;
        Body = text;
        Enabled = enabled;
        UpdatedUtc = nowUtc;
        UpdatedBy = updatedBy.Length > 300 ? updatedBy[..300] : updatedBy;
    }
}

/// <summary>
/// Texts for one organization (Phase 5): on or off, and how many a month at most. An organization
/// without a row uses the defaults (on, Notifications:DefaultMonthlySmsLimit).
/// </summary>
public sealed class TenantMessagingSettings
{
    private TenantMessagingSettings()
    {
        UpdatedBy = "";
    }

    public int TenantId { get; private set; }
    public bool SmsEnabled { get; private set; }
    public int MonthlySmsLimit { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public string UpdatedBy { get; private set; }

    public static TenantMessagingSettings Create(int tenantId, bool smsEnabled, int monthlySmsLimit, DateTime nowUtc, string updatedBy)
    {
        var settings = new TenantMessagingSettings { TenantId = tenantId };
        settings.Change(smsEnabled, monthlySmsLimit, nowUtc, updatedBy);
        return settings;
    }

    public void Change(bool smsEnabled, int monthlySmsLimit, DateTime nowUtc, string updatedBy)
    {
        if (monthlySmsLimit is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(monthlySmsLimit), "0 to 100000 texts a month.");
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        SmsEnabled = smsEnabled;
        MonthlySmsLimit = monthlySmsLimit;
        UpdatedUtc = nowUtc;
        UpdatedBy = updatedBy.Length > 300 ? updatedBy[..300] : updatedBy;
    }
}
