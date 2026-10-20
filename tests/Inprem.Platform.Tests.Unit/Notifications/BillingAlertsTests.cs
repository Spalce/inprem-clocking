using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;
using Xunit;
using static Inprem.Platform.Tests.Unit.Notifications.NotificationServiceTests;

namespace Inprem.Platform.Tests.Unit.Notifications;

/// <summary>
/// Phase 5 step 5.3: who gets which alert (role, then the user's choices), the daily summary, texts-only
/// invoice alerts, the spend warning, and that an alert never fails what raised it.
/// </summary>
public class BillingAlertsTests
{
    private const int Tenant = 7;

    private readonly MovableClock _clock = new(new DateTime(2026, 10, 19, 14, 0, 0, DateTimeKind.Utc));
    private readonly MemoryStore _store = new();
    private readonly FakeDirectory _directory = new();
    private readonly NotificationOptions _options = new() { AppBaseUrl = "https://app.test", AdminBaseUrl = "https://app.test/Provider" };

    private static readonly NotificationRecipient Owner = new(RecipientType.ProviderUser, "p-owner", "owner@inprem.test", null, "Kofi");
    private static readonly NotificationRecipient Billing = new(RecipientType.ProviderUser, "p-billing", "billing@inprem.test", null, "Esi");
    private static readonly NotificationRecipient Support = new(RecipientType.ProviderUser, "p-support", "support@inprem.test", null, "Ama");

    public BillingAlertsTests()
    {
        _directory.Provider.Add((ProviderRole.Owner, Owner));
        _directory.Provider.Add((ProviderRole.Billing, Billing));
        _directory.Provider.Add((ProviderRole.Support, Support));
        _directory.Members.Add(new NotificationRecipient(RecipientType.OrganizationOwner, "org-owner", "boss@alpha.test", null, "Yaw"));
    }

    private ProviderAlertRecipients Recipients => new(_directory, _store);

    private NotificationService Notifications => new(_store, _directory, _options, _clock, Recipients);

    private BillingAlerts Alerts => new(Notifications, Recipients, _store, _directory, _options);

    private void Choose(string userId, bool newSignups = true, AlertDelivery billing = AlertDelivery.Immediate, bool system = true)
    {
        var preferences = ProviderAlertPreferences.Defaults(userId);
        preferences.Change(newSignups, billing, system, _clock.UtcNow);
        _store.Preferences[userId] = preferences;
    }

    private IEnumerable<string> To(string template) => _store.Messages.Where(m => m.TemplateKey == template).Select(m => m.RecipientRef);

    [Fact]
    public async Task A_Failed_Payment_Goes_To_The_Owners_And_To_Inprems_Owners_And_Billing()
    {
        await Alerts.PaymentFailedAsync(Tenant, 15m, _clock.UtcNow);

        var owner = Assert.Single(_store.Messages, m => m.TemplateKey == NotificationTemplates.PaymentFailed);
        Assert.Equal("boss@alpha.test", owner.Destination);
        Assert.Contains("15.00 USD", owner.Body);
        Assert.Contains("https://app.test/Billing", owner.Body);
        Assert.Equal(new[] { "p-owner", "p-billing" }, To(NotificationTemplates.BillingProblem).Order(StringComparer.Ordinal).Reverse());
        Assert.Contains("https://app.test/Provider/Tenants/Details/7", _store.Messages.First(m => m.TemplateKey == NotificationTemplates.BillingProblem).Body);
        Assert.All(_store.Messages.Where(m => m.TemplateKey == NotificationTemplates.BillingProblem), m => Assert.Null(m.TenantId)); // Inprem's messages: not the organization's texts

        await Alerts.PaymentFailedAsync(Tenant, 15m, _clock.UtcNow); // the same day again (a repeated webhook)
        Assert.Equal(3, _store.Messages.Count);
    }

    [Fact]
    public async Task Daily_Summary_Users_Get_One_Summary_Instead()
    {
        Choose("p-billing", billing: AlertDelivery.DailySummary);
        Choose("p-owner", billing: AlertDelivery.Off);
        await Alerts.SuspendedAsync(Tenant, 30m, _clock.UtcNow);
        Assert.Empty(To(NotificationTemplates.BillingProblem)); // nobody wants it at once

        _store.Audit.Add(AuditEntry.Record("paypal:webhook", "subscription.suspended", "s1", "x", _clock.UtcNow.AddHours(-2), Tenant));
        _store.Audit.Add(AuditEntry.Record("paypal:webhook", "subscription.past-due", "s1", "x", _clock.UtcNow.AddHours(-3), Tenant));
        _store.Audit.Add(AuditEntry.Record("paypal:webhook", "subscription.past-due", "s1", "x", _clock.UtcNow.AddDays(-2), Tenant)); // older than a day
        await Alerts.DailySummaryAsync(_clock.UtcNow);
        await Alerts.DailySummaryAsync(_clock.UtcNow); // the job ran twice

        var summary = Assert.Single(_store.Messages, m => m.TemplateKey == NotificationTemplates.BillingSummary);
        Assert.Equal("p-billing", summary.RecipientRef);
        Assert.Contains("2 billing problem", summary.Subject);
        Assert.Contains("Alpha: suspended", summary.Body);
        Assert.Contains("Alpha: payment failed", summary.Body);
    }

    [Fact]
    public async Task No_Summary_On_A_Quiet_Day()
    {
        Choose("p-billing", billing: AlertDelivery.DailySummary);
        await Alerts.DailySummaryAsync(_clock.UtcNow);
        Assert.Empty(_store.Messages);
    }

    [Fact]
    public async Task New_Signups_Go_To_Every_Role_That_Wants_Them()
    {
        Choose("p-support", newSignups: false);
        await Alerts.NewSignupAsync(Tenant, "free trial", "Standard");
        Assert.Equal(new[] { "p-billing", "p-owner" }, To(NotificationTemplates.NewSignup).Order(StringComparer.Ordinal));
        Assert.Contains("Alpha signed up on the website: free trial, plan Standard.", _store.Messages[0].Body);
    }

    [Fact]
    public async Task System_Problems_Go_To_Owners_Only_Once_An_Hour_Even_At_Night()
    {
        _clock.Set(new DateTime(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc)); // night in New York
        await Alerts.SystemProblemAsync("webhook-rejected", "a PayPal webhook failed verification", "details", _clock.UtcNow);
        await Alerts.SystemProblemAsync("webhook-rejected", "a PayPal webhook failed verification", "details", _clock.UtcNow.AddMinutes(10));
        var message = Assert.Single(_store.Messages);
        Assert.Equal(("p-owner", NotificationUrgency.Urgent), (message.RecipientRef, message.Urgency));

        Choose("p-owner", system: false);
        await Alerts.SystemProblemAsync("job-failed:X", "a background job failed: X", "details", _clock.UtcNow);
        Assert.Single(_store.Messages);
    }

    [Fact]
    public async Task Trial_Reminders_Say_When_And_Come_Once()
    {
        var end = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
        await Alerts.TrialEndingAsync(Tenant, end, 7);
        await Alerts.TrialEndingAsync(Tenant, end, 7);
        await Alerts.TrialEndingAsync(Tenant, end, 1);
        var reminders = _store.Messages.Where(m => m.TemplateKey == NotificationTemplates.TrialEnding).ToList();
        Assert.Equal(2, reminders.Count);
        Assert.Equal("The Inprem Clock-in trial of Alpha ends in 7 days", reminders[0].Subject);
        Assert.Equal("The Inprem Clock-in trial of Alpha ends tomorrow", reminders[1].Subject);
        Assert.Contains("26 October 2026", reminders[0].Body);
    }

    [Fact]
    public async Task A_New_Invoice_Is_A_Text_Only_And_Needs_Consent()
    {
        _directory.Members[0] = _directory.Members[0] with { Phone = "+12025550123" };
        await Alerts.InvoiceIssuedAsync(Guid.NewGuid(), Tenant, "INV-100001", 20m, "USD", new DateOnly(2026, 11, 1));
        Assert.DoesNotContain(_store.Messages, m => m.Channel == NotificationChannel.Email);
        Assert.Equal(SkipReasons.NoConsent, Assert.Single(_store.Messages).SkipReason);

        _store.Consents.Add(ConsentRecord.Record("+12025550123", ConsentAction.Grant, Tenant, "org-owner", ConsentSources.WebsiteSignup, ConsentWording.OwnerAlerts, _clock.UtcNow.AddDays(-1), "test"));
        await Alerts.InvoiceIssuedAsync(Guid.NewGuid(), Tenant, "INV-100002", 20m, "USD", new DateOnly(2026, 11, 1));
        Assert.Contains(_store.Messages, m => m.Status == NotificationStatus.Queued && m.Body.Contains("INV-100002 for 20.00 USD is due 1 November 2026"));
    }

    [Fact]
    public async Task Overdue_Invoices_Are_Told_Once_Per_Invoice()
    {
        var id = Guid.NewGuid();
        await Alerts.InvoiceOverdueAsync(id, Tenant, "INV-100003", 5m, "USD", new DateOnly(2026, 10, 1));
        await Alerts.InvoiceOverdueAsync(id, Tenant, "INV-100003", 5m, "USD", new DateOnly(2026, 10, 1));
        Assert.Single(_store.Messages);
        Assert.Equal("Invoice INV-100003 for Alpha is overdue", _store.Messages[0].Subject);
    }

    [Fact]
    public async Task Owners_Hear_Once_When_Texts_Near_The_Daily_Cap()
    {
        _options.DailySpendCapUsd = 0.05m;
        _options.EstimatedSmsCostUsd = 0.02m;
        _directory.Members[0] = _directory.Members[0] with { Phone = "+12025550123" };
        _store.Consents.Add(ConsentRecord.Record("+12025550123", ConsentAction.Grant, Tenant, "org-owner", ConsentSources.WebsiteSignup, ConsentWording.OwnerAlerts, _clock.UtcNow.AddDays(-1), "test"));

        await Alerts.InvoiceIssuedAsync(Guid.NewGuid(), Tenant, "INV-1", 1m, "USD", new DateOnly(2026, 11, 1)); // 0.02 of 0.05: below 80 %
        Assert.Empty(To(NotificationTemplates.SystemProblem));
        await Alerts.InvoiceIssuedAsync(Guid.NewGuid(), Tenant, "INV-2", 1m, "USD", new DateOnly(2026, 11, 1)); // 0.04: at 80 %
        await Alerts.InvoiceIssuedAsync(Guid.NewGuid(), Tenant, "INV-3", 1m, "USD", new DateOnly(2026, 11, 1)); // skipped at the cap: no second warning

        var warning = Assert.Single(_store.Messages, m => m.TemplateKey == NotificationTemplates.SystemProblem);
        Assert.Equal("p-owner", warning.RecipientRef);
        Assert.Contains("close to the daily spend cap", warning.Subject);
        Assert.Equal(SkipReasons.DailySpendCap, _store.Messages.Single(m => m.Body.Contains("INV-3")).SkipReason);
    }

    [Fact]
    public async Task An_Alert_That_Fails_Never_Fails_What_Raised_It()
    {
        var broken = new BillingAlerts(Notifications, new ProviderAlertRecipients(new BrokenDirectory(), _store), _store, new BrokenDirectory(), _options);
        await broken.PaymentFailedAsync(Tenant, 1m, _clock.UtcNow);
        await broken.NewSignupAsync(Tenant, "free trial", "Standard");
        Assert.True(_store.Discarded >= 2);
    }

    private sealed class BrokenDirectory : IRecipientDirectory
    {
        public Task<OrganizationContact?> OrganizationAsync(int tenantId, CancellationToken ct = default) => throw new InvalidOperationException("database down");
        public Task<IReadOnlyList<NotificationRecipient>> OrganizationMembersAsync(int tenantId, bool includeAdmins, CancellationToken ct = default) => throw new InvalidOperationException("database down");
        public Task<IReadOnlyList<NotificationRecipient>> ProviderUsersAsync(IReadOnlyCollection<ProviderRole> roles, CancellationToken ct = default) => throw new InvalidOperationException("database down");
    }
}
