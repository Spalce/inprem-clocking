using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Notifications;

public sealed record NotificationFilter(NotificationChannel? Channel, NotificationStatus? Status, int? TenantId, string? TemplateKey);

/// <summary>One row of the delivery log (the destination already masked for texts).</summary>
public sealed record NotificationRow(Guid Id, DateTime CreatedUtc, int? TenantId, string? Organization, NotificationChannel Channel, string To,
    string TemplateKey, NotificationStatus Status, string? Reason, decimal? Cost, DateTime? SentUtc, int Attempts);

public sealed record OptOutRow(string Phone, DateTime RecordedUtc, string Source);

public sealed record TenantTextsRow(int TenantId, string Organization, int Texts, decimal Cost, bool SmsEnabled, int MonthlyLimit);

/// <summary>A message's wording as the console shows it: the default, or the edited one.</summary>
public sealed record TemplateView(TemplateDefault Default, NotificationChannel Channel, string? Subject, string Body, bool Enabled, bool Edited, DateTime? EditedUtc, string? EditedBy);

/// <summary>The reads the console's Notifications pages need (Phase 5 step 5.3).</summary>
public interface INotificationConsoleStore
{
    Task<(IReadOnlyList<NotificationRow> Items, int Total)> ListAsync(NotificationFilter filter, int skip, int take, CancellationToken ct = default);
    Task<IReadOnlyList<OptOutRow>> OptOutsAsync(int take, CancellationToken ct = default);

    /// <summary>Per organization with texts since <paramref name="sinceUtc"/> or with settings: counts, cost (price or estimate), settings.</summary>
    Task<IReadOnlyList<TenantTextsRow>> TextsByTenantAsync(DateTime sinceUtc, int defaultMonthlyLimit, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationTemplate>> TemplatesAsync(CancellationToken ct = default);
    Task<NotificationTemplate?> FindTemplateForUpdateAsync(string key, NotificationChannel channel, CancellationToken ct = default);
    Task AddTemplateAsync(NotificationTemplate template, CancellationToken ct = default);
    void RemoveTemplate(NotificationTemplate template);
}

/// <summary>
/// The console's Notifications pages (Phase 5 step 5.3): the delivery log, wordings (edited on their
/// own page, or back to the default), opt-outs, the spend, an organization's texts settings, and a
/// test message to oneself. Every change is in the audit log.
/// </summary>
public sealed class NotificationConsole
{
    private readonly INotificationConsoleStore _console;
    private readonly INotificationStore _store;
    private readonly NotificationService _notifications;
    private readonly NotificationOptions _options;
    private readonly IClock _clock;

    public NotificationConsole(INotificationConsoleStore console, INotificationStore store, NotificationService notifications, NotificationOptions options, IClock clock)
    {
        _console = console;
        _store = store;
        _notifications = notifications;
        _options = options;
        _clock = clock;
    }

    public NotificationOptions Options => _options;

    public Task<(IReadOnlyList<NotificationRow> Items, int Total)> LogAsync(NotificationFilter filter, int page, int pageSize, CancellationToken ct = default) =>
        _console.ListAsync(filter, (Math.Max(1, page) - 1) * pageSize, pageSize, ct);

    public Task<IReadOnlyList<OptOutRow>> OptOutsAsync(CancellationToken ct = default) => _console.OptOutsAsync(500, ct);

    /// <summary>Texts this month (UTC) per organization, and today's spend against the cap.</summary>
    public async Task<(IReadOnlyList<TenantTextsRow> Tenants, decimal Today, decimal Cap)> SpendAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var tenants = await _console.TextsByTenantAsync(month, _options.DefaultMonthlySmsLimit, ct).ConfigureAwait(false);
        return (tenants, await _store.SmsSpendAsync(now.Date, ct).ConfigureAwait(false), _options.DailySpendCapUsd);
    }

    public async Task<IReadOnlyList<TemplateView>> TemplatesAsync(CancellationToken ct = default)
    {
        var edited = (await _console.TemplatesAsync(ct).ConfigureAwait(false)).ToDictionary(t => (t.Key, t.Channel));
        var views = new List<TemplateView>();
        foreach (var template in NotificationTemplates.All.OrderBy(t => t.Key, StringComparer.Ordinal))
            foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.Sms })
                if (View(template, channel, edited.GetValueOrDefault((template.Key, channel))) is { } view) views.Add(view);
        return views;
    }

    public async Task<TemplateView?> TemplateAsync(string key, NotificationChannel channel, CancellationToken ct = default)
    {
        var template = NotificationTemplates.Find(key);
        if (template == null) return null;
        return View(template, channel, await _store.FindTemplateAsync(key, channel, ct).ConfigureAwait(false));
    }

    private static TemplateView? View(TemplateDefault template, NotificationChannel channel, NotificationTemplate? edited)
    {
        var body = channel == NotificationChannel.Email ? template.EmailBody : template.SmsBody;
        if (body == null) return null; // the message has no wording for this channel
        return edited != null
            ? new TemplateView(template, channel, edited.Subject, edited.Body, edited.Enabled, true, edited.UpdatedUtc, edited.UpdatedBy)
            : new TemplateView(template, channel, channel == NotificationChannel.Email ? template.EmailSubject : null, body, true, false, null, null);
    }

    /// <summary>Saves an edited wording; null when it was saved, otherwise the problem.</summary>
    public async Task<string?> SaveTemplateAsync(string key, NotificationChannel channel, string? subject, string body, bool enabled, string actor, CancellationToken ct = default)
    {
        var view = await TemplateAsync(key, channel, ct).ConfigureAwait(false);
        if (view == null) return "That message has no wording for this channel.";
        var now = _clock.UtcNow;
        try
        {
            var existing = await _console.FindTemplateForUpdateAsync(key, channel, ct).ConfigureAwait(false);
            if (existing == null) await _console.AddTemplateAsync(NotificationTemplate.Create(key, channel, subject, body, enabled, now, actor), ct).ConfigureAwait(false);
            else existing.Change(subject, body, enabled, now, actor);
        }
        catch (ArgumentException e)
        {
            return e.Message.Split(" (Parameter")[0];
        }
        await _store.AddAuditAsync(AuditEntry.Record(actor, "notification.template-changed", $"{key} ({channel})", enabled ? "Wording changed." : "Wording changed; switched off.", now), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>Back to the default wording (removes the edit). Returns false when there was none.</summary>
    public async Task<bool> ResetTemplateAsync(string key, NotificationChannel channel, string actor, CancellationToken ct = default)
    {
        var existing = await _console.FindTemplateForUpdateAsync(key, channel, ct).ConfigureAwait(false);
        if (existing == null) return false;
        _console.RemoveTemplate(existing);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "notification.template-reset", $"{key} ({channel})", "Back to the default wording.", _clock.UtcNow), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>An organization's texts: on or off, and the monthly limit (Phase 5 step 5.3).</summary>
    public async Task<string?> SetTextsAsync(int tenantId, bool smsEnabled, int monthlyLimit, string actor, CancellationToken ct = default)
    {
        if (monthlyLimit is < 0 or > 100_000) return "The monthly limit must be 0 to 100000 texts.";
        var now = _clock.UtcNow;
        var settings = await _store.FindSettingsForUpdateAsync(tenantId, ct).ConfigureAwait(false);
        if (settings == null) await _store.AddSettingsAsync(TenantMessagingSettings.Create(tenantId, smsEnabled, monthlyLimit, now, actor), ct).ConfigureAwait(false);
        else settings.Change(smsEnabled, monthlyLimit, now, actor);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "organization.texts-changed", $"tenant:{tenantId}",
            $"Texts {(smsEnabled ? "on" : "off")}, at most {monthlyLimit} a month.", now, tenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }

    public async Task<(bool SmsEnabled, int MonthlyLimit)> TextsSettingsAsync(int tenantId, CancellationToken ct = default)
    {
        var settings = await _store.FindSettingsAsync(tenantId, ct).ConfigureAwait(false);
        return (settings?.SmsEnabled ?? true, settings?.MonthlySmsLimit ?? _options.DefaultMonthlySmsLimit);
    }

    /// <summary>The test message to the console user themself: by email, and by text when they agreed to Inprem's texts.</summary>
    public async Task<NotificationRaiseResult> SendTestAsync(string userId, string? email, string? confirmedPhone, string? firstName, CancellationToken ct = default)
    {
        var result = await _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.TestMessage, null, Audience.Explicit,
            Recipients: new[] { new NotificationRecipient(RecipientType.ProviderUser, userId, email, confirmedPhone, firstName) }), ct).ConfigureAwait(false);
        await _store.AddAuditAsync(AuditEntry.Record($"user:{userId}", "notification.test-sent", userId, $"{result.Queued} queued, {result.Skipped.Count} skipped.", _clock.UtcNow), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return result;
    }
}
