using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Notifications;

/// <summary>
/// Settings for messages (section <c>Notifications</c>, Phase 5). Defaults agreed with the developer on
/// 2026-10-19 until the project owner decides: texts on for every organization, at most 50 a month
/// each, at most US$5 of texts a day for the whole platform.
/// </summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>Quiet hours in the organization's own time zone: normal texts wait until they end.</summary>
    public string QuietHoursStart { get; set; } = "21:00";
    public string QuietHoursEnd { get; set; } = "08:00";

    /// <summary>For Inprem's own texts (no organization): the time zone of its quiet hours.</summary>
    public string ProviderTimeZoneId { get; set; } = "America/New_York";

    public int DefaultMonthlySmsLimit { get; set; } = 50;

    /// <summary>The platform's daily spend cap for texts, in US dollars (UTC days).</summary>
    public decimal DailySpendCapUsd { get; set; } = 5.00m;

    /// <summary>What a text is counted at until Twilio reports its real price.</summary>
    public decimal EstimatedSmsCostUsd { get; set; } = 0.02m;

    /// <summary>Attempts before a temporary failure (Twilio down) becomes Failed.</summary>
    public int MaxAttempts { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 120;

    /// <summary>Messages sent per run of the send job.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Above this share of the daily spend cap, Inprem's Owners get a system-problem alert (once a day).</summary>
    public decimal SpendWarningShare { get; set; } = 0.8m;

    /// <summary>For links in messages: copied from Links:AppBaseUrl and Links:AdminBaseUrl at startup.</summary>
    public string? AppBaseUrl { get; set; }
    public string? AdminBaseUrl { get; set; }

    public TimeOnly QuietStart => TimeOnly.Parse(QuietHoursStart, System.Globalization.CultureInfo.InvariantCulture);
    public TimeOnly QuietEnd => TimeOnly.Parse(QuietHoursEnd, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The settings' problems, empty when they are fine (checked at startup).</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!TimeOnly.TryParse(QuietHoursStart, System.Globalization.CultureInfo.InvariantCulture, out _)) problems.Add("Notifications:QuietHoursStart must be a time such as 21:00.");
        if (!TimeOnly.TryParse(QuietHoursEnd, System.Globalization.CultureInfo.InvariantCulture, out _)) problems.Add("Notifications:QuietHoursEnd must be a time such as 08:00.");
        if (Time.TimeZoneResolver.TryFind(ProviderTimeZoneId) == null) problems.Add("Notifications:ProviderTimeZoneId must be a time zone such as America/New_York.");
        if (DefaultMonthlySmsLimit < 0) problems.Add("Notifications:DefaultMonthlySmsLimit cannot be negative.");
        if (DailySpendCapUsd < 0) problems.Add("Notifications:DailySpendCapUsd cannot be negative.");
        if (EstimatedSmsCostUsd <= 0) problems.Add("Notifications:EstimatedSmsCostUsd must be above 0.");
        if (MaxAttempts < 1) problems.Add("Notifications:MaxAttempts must be at least 1.");
        if (RetryDelaySeconds < 1) problems.Add("Notifications:RetryDelaySeconds must be at least 1.");
        if (BatchSize is < 1 or > 1000) problems.Add("Notifications:BatchSize must be 1 to 1000.");
        return problems;
    }
}

/// <summary>Sends one text (Twilio Messaging Service, or the stand-in until Twilio is set up).</summary>
public interface ISmsSender
{
    /// <summary>True for the stand-in: nothing leaves the machine.</summary>
    bool IsStandIn { get; }

    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default);
}

/// <summary>
/// What the sender said. <see cref="Temporary"/>: try again later (Twilio down, too many requests).
/// <see cref="RecipientOptedOut"/>: Twilio refused because the number replied STOP (error 21610).
/// </summary>
public sealed record SmsSendResult(bool Accepted, string? ExternalId, string? Error, bool Temporary, bool RecipientOptedOut = false)
{
    public static SmsSendResult Ok(string externalId) => new(true, externalId, null, false);
    public static SmsSendResult Fail(string error, bool temporary, bool optedOut = false) => new(false, null, error, temporary, optedOut);
}

/// <summary>Someone a message can go to (an account, or a staff member or volunteer in Phase 6).</summary>
public sealed record NotificationRecipient(RecipientType Type, string Ref, string? Email, string? Phone, string? FirstName);

public sealed record OrganizationContact(int TenantId, string Name, string TimeZoneId);

/// <summary>Who receives what: organizations' owners and admins, Inprem's console users.</summary>
public interface IRecipientDirectory
{
    Task<OrganizationContact?> OrganizationAsync(int tenantId, CancellationToken ct = default);

    /// <summary>Active owners; with <paramref name="includeAdmins"/>, active admins too.</summary>
    Task<IReadOnlyList<NotificationRecipient>> OrganizationMembersAsync(int tenantId, bool includeAdmins, CancellationToken ct = default);

    /// <summary>Active console users with one of <paramref name="roles"/> (all roles when empty).</summary>
    Task<IReadOnlyList<NotificationRecipient>> ProviderUsersAsync(IReadOnlyCollection<ProviderRole> roles, CancellationToken ct = default);
}

public interface INotificationStore
{
    Task<bool> ExistsAsync(string eventKey, string recipientRef, NotificationChannel channel, CancellationToken ct = default);
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> DueAsync(DateTime nowUtc, int take, CancellationToken ct = default);
    Task<Notification?> FindByExternalIdAsync(string externalId, CancellationToken ct = default);

    /// <summary>Texts of the organization created since <paramref name="sinceUtc"/>, not counting skipped ones.</summary>
    Task<int> CountSmsAsync(int tenantId, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>What texts created since <paramref name="sinceUtc"/> cost (Twilio's price, or the estimate until known), skipped ones excluded.</summary>
    Task<decimal> SmsSpendAsync(DateTime sinceUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ConsentRecord>> ConsentHistoryAsync(string phoneE164, CancellationToken ct = default);
    Task AddConsentAsync(ConsentRecord record, CancellationToken ct = default);

    Task<NotificationTemplate?> FindTemplateAsync(string key, NotificationChannel channel, CancellationToken ct = default);
    Task<TenantMessagingSettings?> FindSettingsAsync(int tenantId, CancellationToken ct = default);

    /// <summary>The settings row, tracked for a change (null when the organization has none yet).</summary>
    Task<TenantMessagingSettings?> FindSettingsForUpdateAsync(int tenantId, CancellationToken ct = default);
    Task AddSettingsAsync(TenantMessagingSettings settings, CancellationToken ct = default);

    /// <summary>A login-code send or check (platform.SecurityAttempts), for the rate limits.</summary>
    Task AddAttemptAsync(string kind, string accountId, bool succeeded, DateTime nowUtc, CancellationToken ct = default);
    Task<int> CountAttemptsAsync(string kind, string accountId, DateTime sinceUtc, bool succeededOnly = false, bool failedOnly = false, CancellationToken ct = default);

    /// <summary>Console users' alert choices (step 5.3); users without a row are not in the result.</summary>
    Task<IReadOnlyDictionary<string, ProviderAlertPreferences>> PreferencesAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);
    Task<ProviderAlertPreferences?> FindPreferencesForUpdateAsync(string userId, CancellationToken ct = default);
    Task AddPreferencesAsync(ProviderAlertPreferences preferences, CancellationToken ct = default);

    /// <summary>Audit entries with one of the actions since a time (the provider's daily billing summary).</summary>
    Task<IReadOnlyList<AuditEntry>> AuditSinceAsync(IReadOnlyCollection<string> actions, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Forgets messages and consents added but not saved (an alert that failed must not ride along with the next save).</summary>
    void DiscardPending();

    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
