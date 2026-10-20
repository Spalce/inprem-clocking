using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Notifications;

/// <summary>
/// Phase 5 step 5.1: the notification module's rules (P5-2 consent, P5-3 quiet hours and limits),
/// against an in-memory outbox. The send job's rules too: STOP after queueing, retries, Twilio's opt-out answer.
/// </summary>
public class NotificationServiceTests
{
    private const int Tenant = 7;
    private const string Phone = "+12025550123";

    // 14:00 UTC = 10:00 in New York: outside quiet hours.
    private readonly MovableClock _clock = new(new DateTime(2026, 10, 19, 14, 0, 0, DateTimeKind.Utc));
    private readonly MemoryStore _store = new();
    private readonly FakeDirectory _directory = new();
    private readonly NotificationOptions _options = new();

    private NotificationService Service => new(_store, _directory, _options, _clock);

    private NotificationRecipient Owner(string? phone = Phone, string? email = "owner@alpha.test") =>
        new(RecipientType.OrganizationOwner, "owner-1", email, phone, "Ama");

    private void Consent(int? tenant = Tenant, DateTime? at = null) =>
        _store.Consents.Add(ConsentRecord.Record(Phone, ConsentAction.Grant, tenant, "owner-1", ConsentSources.WebsiteSignup, "I agree to texts.", at ?? _clock.UtcNow.AddDays(-1), "test"));

    private static NotificationEvent Event(string? key = null, NotificationUrgency? urgency = null, Audience audience = Audience.OrganizationOwners) =>
        new(NotificationTemplates.TestMessage, Tenant, audience, EventKey: key, Urgency: urgency);

    private Notification Sms => _store.Messages.Single(m => m.Channel == NotificationChannel.Sms);

    // ------------------------------------------------------------------ raise

    [Fact]
    public async Task An_Owner_With_Consent_Gets_An_Email_And_A_Text()
    {
        _directory.Members.Add(Owner("+1 (202) 555-0123"));
        Consent();

        var result = await Service.RaiseAsync(Event());

        Assert.Equal(2, result.Queued);
        Assert.Equal(Phone, Sms.Destination); // stored in E.164
        Assert.Contains("Inprem Clock-in: test message", Sms.Body);
        var email = _store.Messages.Single(m => m.Channel == NotificationChannel.Email);
        Assert.Equal(("owner@alpha.test", "Test message from Inprem Clock-in"), (email.Destination, email.Subject));
        Assert.Contains("Hello Ama,", email.Body);
    }

    [Fact]
    public async Task No_Text_Without_Consent_But_The_Email_Still_Goes()
    {
        _directory.Members.Add(Owner());

        var result = await Service.RaiseAsync(Event());

        Assert.Equal(1, result.Queued);
        Assert.Equal((NotificationStatus.Skipped, SkipReasons.NoConsent), (Sms.Status, Sms.SkipReason));
        Assert.Single(result.Skipped);
    }

    [Fact]
    public async Task Consent_For_Another_Organization_Does_Not_Count()
    {
        _directory.Members.Add(Owner());
        Consent(tenant: 99);
        await Service.RaiseAsync(Event());
        Assert.Equal(SkipReasons.NoConsent, Sms.SkipReason);
    }

    [Fact]
    public async Task After_Stop_No_Text_Is_Queued()
    {
        _directory.Members.Add(Owner());
        Consent();
        _store.Consents.Add(ConsentRecord.Record(Phone, ConsentAction.Revoke, null, null, ConsentSources.SmsStop, null, _clock.UtcNow.AddMinutes(-1), "twilio"));

        await Service.RaiseAsync(Event());

        Assert.Equal(SkipReasons.OptedOut, Sms.SkipReason);
    }

    [Fact]
    public async Task People_Without_A_Number_Get_No_Text_Row_And_A_Bad_Number_Is_Recorded()
    {
        _directory.Members.Add(Owner(phone: null));
        await Service.RaiseAsync(Event());
        Assert.DoesNotContain(_store.Messages, m => m.Channel == NotificationChannel.Sms); // no number given: not "skipped" every time

        _directory.Members.Clear();
        _directory.Members.Add(Owner(phone: "202 555 0123"));
        await Service.RaiseAsync(Event());
        Assert.Equal(SkipReasons.InvalidPhone, Sms.SkipReason);
    }

    [Fact]
    public async Task Normal_Texts_Wait_For_The_End_Of_Quiet_Hours_In_The_Organizations_Time_Zone()
    {
        _directory.Members.Add(Owner());
        Consent();
        _clock.Set(new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc)); // 23:00 in New York

        await Service.RaiseAsync(Event(urgency: NotificationUrgency.Normal));

        Assert.Equal(NotificationStatus.Queued, Sms.Status);
        Assert.Equal(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), Sms.NotBeforeUtc); // 08:00 New York
        Assert.Equal(_clock.UtcNow, _store.Messages.Single(m => m.Channel == NotificationChannel.Email).NotBeforeUtc); // emails do not wait
    }

    [Fact]
    public async Task Urgent_Texts_Go_At_Night_Too()
    {
        _directory.Members.Add(Owner());
        Consent();
        _clock.Set(new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc));
        await Service.RaiseAsync(Event(urgency: NotificationUrgency.Urgent));
        Assert.Equal(_clock.UtcNow, Sms.NotBeforeUtc);
    }

    [Fact]
    public async Task Texts_Switched_Off_For_The_Organization_Are_Skipped()
    {
        _directory.Members.Add(Owner());
        Consent();
        _store.Settings[Tenant] = TenantMessagingSettings.Create(Tenant, smsEnabled: false, 50, _clock.UtcNow, "test");
        await Service.RaiseAsync(Event());
        Assert.Equal(SkipReasons.SmsOff, Sms.SkipReason);
    }

    [Fact]
    public async Task The_Monthly_Limit_Counts_This_Months_Texts()
    {
        _directory.Members.Add(Owner());
        Consent();
        _store.Settings[Tenant] = TenantMessagingSettings.Create(Tenant, smsEnabled: true, monthlySmsLimit: 2, _clock.UtcNow, "test");

        await Service.RaiseAsync(Event("ev-1"));
        await Service.RaiseAsync(Event("ev-2"));
        await Service.RaiseAsync(Event("ev-3"));

        var texts = _store.Messages.Where(m => m.Channel == NotificationChannel.Sms).OrderBy(m => m.EventKey).ToList();
        Assert.Equal(new[] { NotificationStatus.Queued, NotificationStatus.Queued, NotificationStatus.Skipped }, texts.Select(t => t.Status));
        Assert.Equal(SkipReasons.MonthlyLimit, texts[2].SkipReason);
    }

    [Fact]
    public async Task The_Default_Monthly_Limit_Applies_Without_Settings()
    {
        _options.DefaultMonthlySmsLimit = 1;
        _directory.Members.Add(Owner());
        Consent();
        await Service.RaiseAsync(Event("ev-1"));
        await Service.RaiseAsync(Event("ev-2"));
        Assert.Equal(SkipReasons.MonthlyLimit, _store.Messages.Single(m => m.Channel == NotificationChannel.Sms && m.EventKey == "ev-2").SkipReason);
    }

    [Fact]
    public async Task The_Platforms_Daily_Spend_Cap_Stops_Texts_For_Everyone()
    {
        _options.DailySpendCapUsd = 0.05m;
        _options.EstimatedSmsCostUsd = 0.02m;
        _directory.Members.Add(Owner());
        Consent();

        for (var i = 1; i <= 3; i++) await Service.RaiseAsync(Event($"ev-{i}"));

        var texts = _store.Messages.Where(m => m.Channel == NotificationChannel.Sms).OrderBy(m => m.EventKey).ToList();
        Assert.Equal(new[] { null, null, SkipReasons.DailySpendCap }, texts.Select(t => t.SkipReason)); // 0.02 + 0.02 fit, a third would make 0.06
    }

    [Fact]
    public async Task Real_Prices_Replace_Estimates_In_The_Spend_Cap()
    {
        _options.DailySpendCapUsd = 0.05m;
        _directory.Members.Add(Owner());
        Consent();
        await Service.RaiseAsync(Event("ev-1"));
        Sms.Sent("SM1", _clock.UtcNow);
        Sms.ApplyDeliveryReport(DeliveryState.Delivered, null, 0.04m, _clock.UtcNow); // dearer than estimated

        await Service.RaiseAsync(Event("ev-2"));

        Assert.Equal(SkipReasons.DailySpendCap, _store.Messages.Single(m => m.Channel == NotificationChannel.Sms && m.EventKey == "ev-2").SkipReason);
    }

    [Fact]
    public async Task The_Same_Event_Is_Never_Sent_Twice()
    {
        _directory.Members.Add(Owner());
        Consent();
        Assert.Equal(2, (await Service.RaiseAsync(Event("trial-ending-7:7:2026-11-01"))).Queued);
        Assert.Equal(0, (await Service.RaiseAsync(Event("trial-ending-7:7:2026-11-01"))).Queued);
        Assert.Equal(2, _store.Messages.Count);
    }

    [Fact]
    public async Task A_Template_Switched_Off_In_The_Console_Sends_Nothing()
    {
        _directory.Members.Add(Owner());
        Consent();
        _store.Templates.Add(NotificationTemplate.Create(NotificationTemplates.TestMessage, NotificationChannel.Sms, null, "Edited {firstName}", enabled: false, _clock.UtcNow, "test"));
        _store.Templates.Add(NotificationTemplate.Create(NotificationTemplates.TestMessage, NotificationChannel.Email, "Edited subject", "Edited body for {firstName}", enabled: true, _clock.UtcNow, "test"));

        await Service.RaiseAsync(Event());

        Assert.Equal(SkipReasons.TemplateOff, Sms.SkipReason);
        var email = _store.Messages.Single(m => m.Channel == NotificationChannel.Email);
        Assert.Equal(("Edited subject", "Edited body for Ama"), (email.Subject, email.Body));
    }

    [Fact]
    public async Task Inprem_Users_Use_Inprem_Consent_And_Only_Their_Roles()
    {
        _directory.Provider.Add((ProviderRole.Owner, new NotificationRecipient(RecipientType.ProviderUser, "p-owner", "owner@inprem.test", Phone, "Kofi")));
        _directory.Provider.Add((ProviderRole.Support, new NotificationRecipient(RecipientType.ProviderUser, "p-support", "support@inprem.test", null, "Esi")));
        Consent(tenant: null);

        var result = await Service.RaiseAsync(new NotificationEvent(NotificationTemplates.TestMessage, null, Audience.ProviderUsers, ProviderRoles: new[] { ProviderRole.Owner }));

        Assert.Equal(2, result.Queued); // the owner's email and text; Support is not asked for
        Assert.All(_store.Messages, m => Assert.Equal("p-owner", m.RecipientRef));
        Assert.Contains("Hello Kofi", _store.Messages.Single(m => m.Channel == NotificationChannel.Email).Body);
    }

    [Fact]
    public async Task An_Organization_That_No_Longer_Exists_Gets_Nothing()
    {
        _directory.Organization = null;
        Assert.Equal(NotificationRaiseResult.Nothing, await Service.RaiseAsync(Event()));
    }

    [Fact]
    public async Task Consent_Is_Recorded_Only_For_A_Full_Number()
    {
        Assert.True(await Service.RecordConsentAsync("+1 202 555 0123", Tenant, "owner-1", ConsentSources.WebsiteSignup, "I agree.", "signup:1"));
        Assert.False(await Service.RecordConsentAsync("202 555 0123", Tenant, "owner-1", ConsentSources.WebsiteSignup, "I agree.", "signup:1"));
        Assert.Equal(Phone, Assert.Single(_store.Consents).Phone);
        Assert.Equal("notifications.consent-granted", Assert.Single(_store.Audit).Action);
        Assert.DoesNotContain("5550123", _store.Audit[0].Target); // masked
    }

    [Fact]
    public async Task The_Sign_In_Code_Switch_Is_Off_Until_Switched_On_And_Audited()
    {
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var settings = new MessagingSettingsService(_store, cache, _options, _clock);
        Assert.False(await settings.SmsSignInRequiredAsync(Tenant));
        Assert.True(await settings.SetSmsSignInAsync(Tenant, true, "provider:p1"));
        Assert.False(await settings.SetSmsSignInAsync(Tenant, true, "provider:p1")); // already on: nothing new
        Assert.True(await settings.SmsSignInRequiredAsync(Tenant)); // the cache was cleared
        Assert.Equal((true, 50), (_store.Settings[Tenant].SmsEnabled, _store.Settings[Tenant].MonthlySmsLimit)); // texts keep their defaults
        Assert.Equal("organization.sms-sign-in-on", Assert.Single(_store.Audit).Action);
    }

    // ------------------------------------------------------------------ send

    private readonly FakeSms _sms = new();
    private readonly FakeEmail _email = new();
    private NotificationSender Sender => new(_store, _sms, _email, _options, _clock);

    [Fact]
    public async Task The_Send_Job_Sends_Due_Messages_And_Leaves_The_Rest_Waiting()
    {
        _directory.Members.Add(Owner());
        Consent();
        _clock.Set(new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc)); // night in New York
        await Service.RaiseAsync(Event(urgency: NotificationUrgency.Normal));

        var night = await Sender.SendDueAsync();
        Assert.Equal(new SendReport(1, 0, 0, 0), night); // the email; the text waits
        Assert.Empty(_sms.Sent);
        Assert.Contains("<p>Hello Ama,</p>", Assert.Single(_email.Sent).Html);

        _clock.Set(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new SendReport(1, 0, 0, 0), await Sender.SendDueAsync());
        Assert.Equal(Phone, Assert.Single(_sms.Sent));
        Assert.Equal((NotificationStatus.Sent, "SM-fake-1"), (Sms.Status, Sms.ExternalId));
    }

    [Fact]
    public async Task A_Stop_After_Queueing_Wins_At_Send_Time()
    {
        _directory.Members.Add(Owner());
        Consent();
        await Service.RaiseAsync(Event(urgency: NotificationUrgency.Urgent, key: "k"));
        _clock.Set(_clock.UtcNow.AddMinutes(1));
        await new TwilioReports(_store, _clock).InboundAsync(Phone, "stop");

        var report = await Sender.SendDueAsync();

        Assert.Equal(1, report.Skipped);
        Assert.Empty(_sms.Sent);
        Assert.Equal(SkipReasons.OptedOut, Sms.SkipReason);
    }

    [Fact]
    public async Task Twilio_Down_Is_Tried_Again_Later_Then_Given_Up()
    {
        _options.MaxAttempts = 2;
        _directory.Members.Add(Owner(email: null));
        Consent();
        await Service.RaiseAsync(Event());
        _sms.Answers.Enqueue(SmsSendResult.Fail("Twilio could not be reached.", temporary: true));
        _sms.Answers.Enqueue(SmsSendResult.Fail("Twilio could not be reached.", temporary: true));

        Assert.Equal(1, (await Sender.SendDueAsync()).Retrying);
        Assert.Equal(0, (await Sender.SendDueAsync()).Retrying + (await Sender.SendDueAsync()).Failed); // not due yet
        _clock.Set(_clock.UtcNow.AddHours(1));
        Assert.Equal(1, (await Sender.SendDueAsync()).Failed);
        Assert.Equal((NotificationStatus.Failed, 2), (Sms.Status, Sms.Attempts));
    }

    [Fact]
    public async Task Twilios_Opt_Out_Answer_Records_The_Stop()
    {
        _directory.Members.Add(Owner(email: null));
        Consent();
        await Service.RaiseAsync(Event());
        _sms.Answers.Enqueue(SmsSendResult.Fail("Twilio refused the text (HTTP 400, error 21610).", temporary: false, optedOut: true));

        await Sender.SendDueAsync();

        Assert.Equal(SkipReasons.OptedOut, Sms.SkipReason);
        Assert.Equal(ConsentState.Withdrawn, ConsentRecord.StateFor(_store.Consents, Tenant));
    }

    [Fact]
    public async Task An_Email_That_Fails_Is_Tried_Again()
    {
        _directory.Members.Add(Owner(phone: null));
        await Service.RaiseAsync(Event());
        _email.Fail = true;
        Assert.Equal(1, (await Sender.SendDueAsync()).Retrying);
        _email.Fail = false;
        _clock.Set(_clock.UtcNow.AddHours(1));
        Assert.Equal(1, (await Sender.SendDueAsync()).Sent);
    }

    // ------------------------------------------------------------------ Twilio's reports

    [Theory]
    [InlineData("STOP", InboundOutcome.OptedOut)]
    [InlineData(" Unsubscribe. ", InboundOutcome.OptedOut)]
    [InlineData("start", InboundOutcome.Resubscribed)]
    [InlineData("HELP", InboundOutcome.Help)]
    [InlineData("Thanks, see you tomorrow", InboundOutcome.Ignored)]
    public async Task Replies_Are_Read_As_Keywords_Only(string body, InboundOutcome expected) =>
        Assert.Equal(expected, await new TwilioReports(_store, _clock).InboundAsync(Phone, body));

    [Fact]
    public async Task Delivery_Reports_Find_The_Message_By_Twilios_Id()
    {
        _directory.Members.Add(Owner(email: null));
        Consent();
        await Service.RaiseAsync(Event());
        await Sender.SendDueAsync();
        var reports = new TwilioReports(_store, _clock);

        Assert.True(await reports.ApplyStatusAsync("SM-fake-1", "delivered", null, "-0.00790"));
        Assert.Equal((NotificationStatus.Delivered, 0.0079m), (Sms.Status, Sms.Cost));
        Assert.False(await reports.ApplyStatusAsync("SM-unknown", "delivered", null, null));
        Assert.False(await reports.ApplyStatusAsync("SM-fake-1", "sent", null, null)); // late: nothing changes
    }

    // ------------------------------------------------------------------ fakes

    internal sealed class MovableClock : IClock
    {
        public MovableClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; private set; }
        public void Set(DateTime now) => UtcNow = now;
    }

    internal sealed class FakeDirectory : IRecipientDirectory
    {
        public OrganizationContact? Organization { get; set; } = new(Tenant, "Alpha", "America/New_York");
        public List<NotificationRecipient> Members { get; } = new();
        public List<(ProviderRole Role, NotificationRecipient Recipient)> Provider { get; } = new();

        public Task<OrganizationContact?> OrganizationAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(Organization?.TenantId == tenantId ? Organization : null);

        public Task<IReadOnlyList<NotificationRecipient>> OrganizationMembersAsync(int tenantId, bool includeAdmins, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationRecipient>>(Members.Where(m => includeAdmins || m.Type == RecipientType.OrganizationOwner).ToList());

        public Task<IReadOnlyList<NotificationRecipient>> ProviderUsersAsync(IReadOnlyCollection<ProviderRole> roles, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationRecipient>>(Provider.Where(p => roles.Count == 0 || roles.Contains(p.Role)).Select(p => p.Recipient).ToList());
    }

    internal sealed class MemoryStore : INotificationStore
    {
        public List<Notification> Messages { get; } = new();
        public List<ConsentRecord> Consents { get; } = new();
        public List<NotificationTemplate> Templates { get; } = new();
        public Dictionary<int, TenantMessagingSettings> Settings { get; } = new();
        public List<AuditEntry> Audit { get; } = new();

        public Task<bool> ExistsAsync(string eventKey, string recipientRef, NotificationChannel channel, CancellationToken ct = default) =>
            Task.FromResult(Messages.Any(m => m.EventKey == eventKey && m.RecipientRef == recipientRef && m.Channel == channel));
        public Task AddAsync(Notification notification, CancellationToken ct = default) { Messages.Add(notification); return Task.CompletedTask; }
        public Task<IReadOnlyList<Notification>> DueAsync(DateTime nowUtc, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(Messages.Where(m => m.Status == NotificationStatus.Queued && m.NotBeforeUtc <= nowUtc).OrderBy(m => m.NotBeforeUtc).Take(take).ToList());
        public Task<Notification?> FindByExternalIdAsync(string externalId, CancellationToken ct = default) => Task.FromResult(Messages.FirstOrDefault(m => m.ExternalId == externalId));
        public Task<int> CountSmsAsync(int tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
            Task.FromResult(Messages.Count(m => m.TenantId == tenantId && m.Channel == NotificationChannel.Sms && m.CreatedUtc >= sinceUtc && m.Status != NotificationStatus.Skipped));
        public Task<decimal> SmsSpendAsync(DateTime sinceUtc, CancellationToken ct = default) =>
            Task.FromResult(Messages.Where(m => m.Channel == NotificationChannel.Sms && m.CreatedUtc >= sinceUtc && m.Status != NotificationStatus.Skipped).Sum(m => m.Cost ?? m.EstimatedCost));
        public Task<IReadOnlyList<ConsentRecord>> ConsentHistoryAsync(string phoneE164, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConsentRecord>>(Consents.Where(c => c.Phone == phoneE164).ToList());
        public Task AddConsentAsync(ConsentRecord record, CancellationToken ct = default) { Consents.Add(record); return Task.CompletedTask; }
        public Task<NotificationTemplate?> FindTemplateAsync(string key, NotificationChannel channel, CancellationToken ct = default) =>
            Task.FromResult(Templates.FirstOrDefault(t => t.Key == key && t.Channel == channel));
        public Task<TenantMessagingSettings?> FindSettingsAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(Settings.GetValueOrDefault(tenantId));
        public Task<TenantMessagingSettings?> FindSettingsForUpdateAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(Settings.GetValueOrDefault(tenantId));
        public Task AddSettingsAsync(TenantMessagingSettings settings, CancellationToken ct = default) { Settings[settings.TenantId] = settings; return Task.CompletedTask; }
        public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) { Audit.Add(entry); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Dictionary<string, ProviderAlertPreferences> Preferences { get; } = new();
        public Task<IReadOnlyDictionary<string, ProviderAlertPreferences>> PreferencesAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ProviderAlertPreferences>>(Preferences.Where(p => userIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
        public Task<ProviderAlertPreferences?> FindPreferencesForUpdateAsync(string userId, CancellationToken ct = default) => Task.FromResult(Preferences.GetValueOrDefault(userId));
        public Task AddPreferencesAsync(ProviderAlertPreferences preferences, CancellationToken ct = default) { Preferences[preferences.UserId] = preferences; return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEntry>> AuditSinceAsync(IReadOnlyCollection<string> actions, DateTime sinceUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Audit.Where(a => actions.Contains(a.Action) && a.OccurredUtc >= sinceUtc).ToList());
        public int Discarded { get; private set; }
        public void DiscardPending() => Discarded++;

        public List<(string Kind, string Account, bool Succeeded, DateTime At)> Attempts { get; } = new();
        public Task AddAttemptAsync(string kind, string accountId, bool succeeded, DateTime nowUtc, CancellationToken ct = default) { Attempts.Add((kind, accountId, succeeded, nowUtc)); return Task.CompletedTask; }
        public Task<int> CountAttemptsAsync(string kind, string accountId, DateTime sinceUtc, bool succeededOnly = false, bool failedOnly = false, CancellationToken ct = default) =>
            Task.FromResult(Attempts.Count(a => a.Kind == kind && a.Account == accountId && a.At > sinceUtc && (!succeededOnly || a.Succeeded) && (!failedOnly || !a.Succeeded)));
    }

    private sealed class FakeSms : ISmsSender
    {
        private int _count;
        public List<string> Sent { get; } = new();
        public Queue<SmsSendResult> Answers { get; } = new();
        public bool IsStandIn => true;

        public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
        {
            if (Answers.TryDequeue(out var answer)) return Task.FromResult(answer);
            Sent.Add(toE164);
            return Task.FromResult(SmsSendResult.Ok($"SM-fake-{++_count}"));
        }
    }

    internal sealed class FakeEmail : IEmailSender
    {
        public List<(string To, string Subject, string Html)> Sent { get; } = new();
        public bool Fail { get; set; }

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("The mail server is down.");
            Sent.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }
    }
}
