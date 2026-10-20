using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;
using Xunit;
using static Inprem.Platform.Tests.Unit.Notifications.NotificationServiceTests;

namespace Inprem.Platform.Tests.Unit.Notifications;

/// <summary>
/// Phase 6 step 6.3 (P6-3): the staff alerts check. Missed clock-out at the organization's local time
/// in two time zones, also on clock-change days; long breaks; the admins' one-message summary; consent
/// and STOP through the notification module; quiet hours drop a staff text instead of holding it.
/// </summary>
public class StaffAlertsCheckTests
{
    private const int NewYork = 1;
    private const int London = 2;
    private const string AmaPhone = "+12025550101";
    private const string KofiPhone = "+447700900102";

    private readonly MovableClock _clock = new(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));
    private readonly MemoryStore _store = new();
    private readonly Directory _directory = new();
    private readonly FakeSource _source = new();
    private readonly NotificationOptions _options = new();

    // Before quiet hours (21:00), as the settings page advises.
    private static readonly TimeOnly At2030 = new(20, 30);

    public StaffAlertsCheckTests()
    {
        _directory.Organizations[NewYork] = new(NewYork, "Alpha", "America/New_York");
        _directory.Organizations[London] = new(London, "Beta", "Europe/London");
    }

    private StaffAlertsCheck Check => new(_source, new NotificationService(_store, _directory, _options, _clock), _store, _clock);

    private void AlertsOn(int tenant, TimeOnly? missed = null, int longBreak = 60, bool summary = false) =>
        _source.Organizations.Add(new StaffAlertOrganization(tenant, _directory.Organizations[tenant].TimeZoneId,
            new StaffAlertChoices(true, missed ?? At2030, longBreak, summary)));

    private void Consent(string phone, int tenant) =>
        _store.Consents.Add(ConsentRecord.Record(phone, ConsentAction.Grant, tenant, "staff:1", ConsentSources.KioskRegistration, ConsentWording.StaffAlerts, _clock.UtcNow.AddDays(-30), "test"));

    private void Open(int tenant, DateOnly day, string personRef, string phone, DateTime clockInUtc, DateTime? breakUtc = null, string first = "Ama") =>
        _source.Add(tenant, day, new OpenClocking(personRef, first, $"{first} Mensah", phone, clockInUtc, breakUtc));

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private List<Notification> Texts(string template) => _store.Messages.Where(m => m.TemplateKey == template && m.Channel == NotificationChannel.Sms).ToList();

    // ------------------------------------------------------------------ missed clock-out

    [Fact]
    public async Task Missed_Clock_Out_Fires_At_The_Local_Time_In_New_York_And_In_London()
    {
        AlertsOn(NewYork);
        AlertsOn(London);
        Consent(AmaPhone, NewYork);
        Consent(KofiPhone, London);
        var day = new DateOnly(2026, 10, 19); // summer time in both: New York UTC-4, London UTC+1
        Open(NewYork, day, "staff:1", AmaPhone, Utc(10, 19, 13));
        Open(London, day, "volunteer:2", KofiPhone, Utc(10, 19, 8), first: "Kofi");

        _clock.Set(Utc(10, 19, 19, 25)); // London 20:25
        Assert.Equal(0, (await Check.RunAsync()).Raised);

        _clock.Set(Utc(10, 19, 19, 30)); // London 20:30, New York 15:30
        var london = await Check.RunAsync();
        Assert.Equal(1, london.MissedClockOuts);
        var text = Assert.Single(Texts(NotificationTemplates.MissedClockOut));
        Assert.Equal((London, KofiPhone, NotificationStatus.Queued), (text.TenantId, text.Destination, text.Status));
        Assert.Equal("Beta: you are still clocked in (since 09:00). Please clock out at the kiosk, or tell an admin. Reply STOP to opt out.", text.Body);

        _clock.Set(Utc(10, 20, 0, 30)); // New York 20:30 on the 19th (London is already on the 20th)
        Assert.Equal(1, (await Check.RunAsync()).MissedClockOuts);
        var newYork = Texts(NotificationTemplates.MissedClockOut).Single(t => t.TenantId == NewYork);
        Assert.Equal(AmaPhone, newYork.Destination);
        Assert.Contains("(since 09:00)", newYork.Body);

        // Later runs the same evening send nothing again.
        _clock.Set(Utc(10, 20, 0, 55));
        Assert.Equal(0, (await Check.RunAsync()).Raised);
        Assert.Equal(2, Texts(NotificationTemplates.MissedClockOut).Count);
    }

    [Theory]
    // New York, clocks go back on 1 November 2026 (EDT to EST): 20:30 is 01:30 UTC the next day, not 00:30.
    [InlineData(NewYork, 2026, 11, 1, 2026, 11, 2, 0, 30, 1, 30)]
    // New York, clocks go forward on 8 March 2026 (EST to EDT): 20:30 is 00:30 UTC, not 01:30.
    [InlineData(NewYork, 2026, 3, 8, 2026, 3, 9, 0, 0, 0, 30)]
    // London, clocks go back on 25 October 2026 (BST to GMT): 20:30 is 20:30 UTC, not 19:30.
    [InlineData(London, 2026, 10, 25, 2026, 10, 25, 19, 30, 20, 30)]
    public async Task On_A_Clock_Change_Day_The_Alert_Follows_The_New_Local_Time(int tenant, int y, int m, int d, int uy, int um, int ud, int beforeH, int beforeM, int atH, int atM)
    {
        AlertsOn(tenant);
        var phone = tenant == NewYork ? AmaPhone : KofiPhone;
        Consent(phone, tenant);
        var day = new DateOnly(y, m, d);
        Open(tenant, day, "staff:1", phone, new DateTime(y, m, d, 14, 0, 0, DateTimeKind.Utc));

        _clock.Set(new DateTime(uy, um, ud, beforeH, beforeM, 0, DateTimeKind.Utc)); // not yet 20:30 by the new offset
        Assert.Equal(0, (await Check.RunAsync()).Raised);

        _clock.Set(new DateTime(uy, um, ud, atH, atM, 0, DateTimeKind.Utc));
        Assert.Equal(1, (await Check.RunAsync()).MissedClockOuts);
        Assert.Equal(NotificationStatus.Queued, Assert.Single(Texts(NotificationTemplates.MissedClockOut)).Status);
    }

    [Fact]
    public async Task Someone_Who_Clocked_In_After_The_Set_Time_Is_Not_Told_They_Missed_It()
    {
        AlertsOn(NewYork);
        Consent(AmaPhone, NewYork);
        var day = new DateOnly(2026, 10, 19);
        Open(NewYork, day, "staff:1", AmaPhone, Utc(10, 20, 0, 40)); // 20:40 local: an evening shift

        _clock.Set(Utc(10, 20, 0, 45));
        Assert.Equal(0, (await Check.RunAsync()).Raised);
    }

    [Fact]
    public async Task At_The_Default_21_00_The_Text_Falls_In_Quiet_Hours_And_Is_Dropped_Not_Held()
    {
        AlertsOn(NewYork, missed: TenantMessagingSettings.DefaultMissedClockOut);
        Consent(AmaPhone, NewYork);
        Open(NewYork, new DateOnly(2026, 10, 19), "staff:1", AmaPhone, Utc(10, 19, 13));

        _clock.Set(Utc(10, 20, 1, 0)); // 21:00 in New York
        Assert.Equal(0, (await Check.RunAsync()).MissedClockOuts);
        var text = Assert.Single(Texts(NotificationTemplates.MissedClockOut));
        Assert.Equal((NotificationStatus.Skipped, SkipReasons.QuietHours), (text.Status, text.SkipReason));

        // Not tried again later that night, and nothing waits for the morning.
        _clock.Set(Utc(10, 20, 1, 30));
        await Check.RunAsync();
        Assert.Single(_store.Messages);
        Assert.DoesNotContain(_store.Messages, m => m.Status == NotificationStatus.Queued);
    }

    [Fact]
    public async Task No_Text_Without_Consent_And_None_After_Stop()
    {
        AlertsOn(NewYork);
        var day = new DateOnly(2026, 10, 19);
        Open(NewYork, day, "staff:1", AmaPhone, Utc(10, 19, 13));
        Open(NewYork, day, "staff:2", "+12025550199", Utc(10, 19, 13), first: "Esi");
        Consent(AmaPhone, NewYork);
        _store.Consents.Add(ConsentRecord.Record(AmaPhone, ConsentAction.Revoke, null, null, ConsentSources.SmsStop, null, _clock.UtcNow.AddDays(-1), "twilio"));

        _clock.Set(Utc(10, 20, 0, 30));
        Assert.Equal(0, (await Check.RunAsync()).Raised);
        var reasons = Texts(NotificationTemplates.MissedClockOut).ToDictionary(t => t.RecipientRef, t => t.SkipReason);
        Assert.Equal(SkipReasons.OptedOut, reasons["staff:1"]);
        Assert.Equal(SkipReasons.NoConsent, reasons["staff:2"]);
    }

    [Fact]
    public async Task Staff_Alerts_Are_Texts_Only_And_Go_To_The_Person_Not_The_Admins()
    {
        AlertsOn(NewYork);
        Consent(AmaPhone, NewYork);
        _directory.Admins.Add(new NotificationRecipient(RecipientType.OrganizationAdmin, "admin-1", "admin@alpha.test", null, "Yaw"));
        Open(NewYork, new DateOnly(2026, 10, 19), "staff:1", AmaPhone, Utc(10, 19, 13));

        _clock.Set(Utc(10, 20, 0, 30));
        await Check.RunAsync();
        var message = Assert.Single(_store.Messages);
        Assert.Equal((NotificationChannel.Sms, RecipientType.Person, "staff:1"), (message.Channel, message.RecipientType, message.RecipientRef));
    }

    // ------------------------------------------------------------------ long break

    [Fact]
    public async Task A_Break_Longer_Than_The_Limit_Gets_One_Text()
    {
        AlertsOn(London, longBreak: 45);
        Consent(KofiPhone, London);
        var day = new DateOnly(2026, 10, 19);
        Open(London, day, "volunteer:2", KofiPhone, Utc(10, 19, 8), breakUtc: Utc(10, 19, 11, 0), first: "Kofi");

        _clock.Set(Utc(10, 19, 11, 44));
        Assert.Equal(0, (await Check.RunAsync()).Raised);

        _clock.Set(Utc(10, 19, 11, 45));
        Assert.Equal(1, (await Check.RunAsync()).LongBreaks);
        var text = Assert.Single(Texts(NotificationTemplates.LongBreak));
        Assert.Equal("Beta: your break started at 12:00 and is still open. Please end it at the kiosk when you are back. Reply STOP to opt out.", text.Body);

        _clock.Set(Utc(10, 19, 12, 30));
        Assert.Equal(0, (await Check.RunAsync()).Raised);
    }

    // ------------------------------------------------------------------ admins' summary

    [Fact]
    public async Task The_Admins_Get_One_Email_Listing_Everyone_Still_Clocked_In()
    {
        AlertsOn(NewYork, summary: true);
        _directory.Admins.Add(new NotificationRecipient(RecipientType.OrganizationOwner, "owner-1", "owner@alpha.test", "+12025550150", "Abena"));
        _directory.Admins.Add(new NotificationRecipient(RecipientType.OrganizationAdmin, "admin-1", "admin@alpha.test", null, "Yaw"));
        var day = new DateOnly(2026, 10, 19);
        Open(NewYork, day, "staff:1", AmaPhone, Utc(10, 19, 13));
        Open(NewYork, day, "volunteer:3", "+12025550103", Utc(10, 19, 12, 15), breakUtc: Utc(10, 19, 23, 50), first: "Kwame");

        _clock.Set(Utc(10, 20, 0, 30));
        Assert.Equal(1, (await Check.RunAsync()).Summaries);
        _clock.Set(Utc(10, 20, 0, 35));
        await Check.RunAsync();

        var emails = _store.Messages.Where(m => m.TemplateKey == NotificationTemplates.ShiftSummary).ToList();
        Assert.Equal(2, emails.Count); // one each for the owner and the admin, once
        Assert.All(emails, e => Assert.Equal(NotificationChannel.Email, e.Channel));
        var owner = emails.Single(e => e.Destination == "owner@alpha.test");
        Assert.Equal("Alpha: 2 still clocked in at 20:30", owner.Subject);
        Assert.Contains("At 20:30 on 19 October 2026", owner.Body);
        Assert.Contains("- Kwame Mensah, since 08:15 (on a break since 19:50)\n- Ama Mensah, since 09:00", owner.Body);
        Assert.DoesNotContain(_store.Messages, m => m.TemplateKey == NotificationTemplates.ShiftSummary && m.Channel == NotificationChannel.Sms);
    }

    [Fact]
    public async Task No_Summary_When_It_Is_Off_Or_Nobody_Is_Still_Clocked_In()
    {
        AlertsOn(NewYork, summary: false);
        AlertsOn(London, summary: true);
        _directory.Admins.Add(new NotificationRecipient(RecipientType.OrganizationAdmin, "admin-1", "admin@alpha.test", null, "Yaw"));
        Open(NewYork, new DateOnly(2026, 10, 19), "staff:1", AmaPhone, Utc(10, 19, 13));

        _clock.Set(Utc(10, 20, 0, 30));
        Assert.Equal(0, (await Check.RunAsync()).Summaries);
        Assert.DoesNotContain(_store.Messages, m => m.TemplateKey == NotificationTemplates.ShiftSummary);
    }

    // ------------------------------------------------------------------ robustness

    [Fact]
    public async Task One_Organization_Failing_Does_Not_Stop_The_Others()
    {
        AlertsOn(NewYork);
        AlertsOn(London);
        Consent(KofiPhone, London);
        _source.FailFor = NewYork;
        Open(London, new DateOnly(2026, 10, 19), "staff:1", KofiPhone, Utc(10, 19, 8));

        _clock.Set(Utc(10, 19, 19, 30));
        var report = await Check.RunAsync();
        Assert.Equal((2, 1), (report.Organizations, report.MissedClockOuts));
        Assert.Equal(1, _store.Discarded);
    }

    [Fact]
    public async Task Nothing_When_No_Organization_Has_Alerts_On()
    {
        Open(NewYork, new DateOnly(2026, 10, 19), "staff:1", AmaPhone, Utc(10, 19, 13));
        _clock.Set(Utc(10, 20, 0, 30));
        Assert.Equal(new StaffAlertsReport(0, 0, 0, 0), await Check.RunAsync());
        Assert.Empty(_store.Messages);
    }

    private sealed class FakeSource : IStaffAlertSource
    {
        private readonly Dictionary<(int, DateOnly), List<OpenClocking>> _open = new();
        public List<StaffAlertOrganization> Organizations { get; } = new();
        public int? FailFor { get; set; }

        public void Add(int tenant, DateOnly day, OpenClocking clocking)
        {
            if (!_open.TryGetValue((tenant, day), out var list)) _open[(tenant, day)] = list = new();
            list.Add(clocking);
        }

        public Task<IReadOnlyList<StaffAlertOrganization>> OrganizationsWithAlertsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StaffAlertOrganization>>(Organizations);

        public Task<IReadOnlyList<OpenClocking>> OpenClockingsAsync(int tenantId, DateOnly clockDate, CancellationToken ct = default) =>
            tenantId == FailFor
                ? throw new InvalidOperationException("The database is down for this organization.")
                : Task.FromResult<IReadOnlyList<OpenClocking>>(_open.GetValueOrDefault((tenantId, clockDate)) ?? new List<OpenClocking>());
    }

    private sealed class Directory : IRecipientDirectory
    {
        public Dictionary<int, OrganizationContact> Organizations { get; } = new();
        public List<NotificationRecipient> Admins { get; } = new();

        public Task<OrganizationContact?> OrganizationAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(Organizations.GetValueOrDefault(tenantId));

        public Task<IReadOnlyList<NotificationRecipient>> OrganizationMembersAsync(int tenantId, bool includeAdmins, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationRecipient>>(tenantId == NewYork ? Admins.Where(a => includeAdmins || a.Type == RecipientType.OrganizationOwner).ToList() : new List<NotificationRecipient>());

        public Task<IReadOnlyList<NotificationRecipient>> ProviderUsersAsync(IReadOnlyCollection<ProviderRole> roles, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationRecipient>>(new List<NotificationRecipient>());
    }
}
