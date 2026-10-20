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
/// Texts for one organization (Phase 5): on or off, and how many a month at most; and whether its
/// admins must also enter a texted code when they sign in (step 5.2, off by default). An organization
/// without a row uses the defaults (on, Notifications:DefaultMonthlySmsLimit, no sign-in codes).
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

    /// <summary>Admins enter a texted code after their password (step 5.2).</summary>
    public bool SmsSignInRequired { get; private set; }

    /// <summary>Phase 6 (C3-8, off by default): texts to staff and volunteers who agreed, and the consent fields on their forms.</summary>
    public bool StaffAlertsEnabled { get; private set; }

    /// <summary>Who is still clocked in at this local time gets a "you are still clocked in" text (D32: 21:00).</summary>
    public TimeOnly MissedClockOutTime { get; private set; } = DefaultMissedClockOut;

    /// <summary>A break longer than this many minutes gets a reminder (D32: 60).</summary>
    public int LongBreakMinutes { get; private set; } = DefaultLongBreakMinutes;

    /// <summary>At the missed clock-out time, one message to the admins: who is still clocked in.</summary>
    public bool AdminSummaryEnabled { get; private set; }

    public static readonly TimeOnly DefaultMissedClockOut = new(21, 0);
    public const int DefaultLongBreakMinutes = 60;
    public const int MinLongBreakMinutes = 10;
    public const int MaxLongBreakMinutes = 720;
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

    /// <summary>The staff and volunteer alerts (Phase 6 step 6.1).</summary>
    public void SetStaffAlerts(bool enabled, TimeOnly missedClockOut, int longBreakMinutes, bool adminSummary, DateTime nowUtc, string updatedBy)
    {
        if (longBreakMinutes is < MinLongBreakMinutes or > MaxLongBreakMinutes)
            throw new ArgumentOutOfRangeException(nameof(longBreakMinutes), $"A long break is {MinLongBreakMinutes} to {MaxLongBreakMinutes} minutes.");
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        StaffAlertsEnabled = enabled;
        MissedClockOutTime = new TimeOnly(missedClockOut.Hour, missedClockOut.Minute);
        LongBreakMinutes = longBreakMinutes;
        AdminSummaryEnabled = adminSummary;
        UpdatedUtc = nowUtc;
        UpdatedBy = updatedBy.Length > 300 ? updatedBy[..300] : updatedBy;
    }

    /// <summary>Switches the texted sign-in code for the organization's admins on or off. Returns false when nothing changed.</summary>
    public bool RequireSmsSignIn(bool required, DateTime nowUtc, string updatedBy)
    {
        if (SmsSignInRequired == required) return false;
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        SmsSignInRequired = required;
        UpdatedUtc = nowUtc;
        UpdatedBy = updatedBy.Length > 300 ? updatedBy[..300] : updatedBy;
        return true;
    }
}
