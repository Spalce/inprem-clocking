using System.Net;
using System.Text;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Notifications;

/// <summary>
/// Phase 5 step 5.3 end to end, with the database and the pretend PayPal: payment failed and suspended
/// (owners and Inprem, once), the daily summary chosen on "My alerts", trial reminders 7 days and the
/// day before, invoice texts and overdue invoices, and a webhook that fails verification.
/// </summary>
public class BillingAlertsFlowTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public BillingAlertsFlowTests(ProviderFixture app) => _app = app;

    private async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<ReconciliationReport> DailyAsync() => UseAsync(sp => sp.GetRequiredService<BillingReconciliation>().DailyAsync());

    private Task<SendReport> SendAsync() => UseAsync(sp => sp.GetRequiredService<NotificationSender>().SendDueAsync());

    /// <summary>
    /// Sends everything queued, also texts held for quiet hours (the test may run at night): the clock
    /// moves past the latest waiting time, then back.
    /// </summary>
    private async Task SendAllAsync()
    {
        await using var db = _app.CreatePlatformDbContext();
        var latest = await db.Notifications.AsNoTracking().Where(n => n.Status == NotificationStatus.Queued).MaxAsync(n => (DateTime?)n.NotBeforeUtc);
        var ahead = latest is { } at && at > _app.Clock.UtcNow ? at - _app.Clock.UtcNow + TimeSpan.FromMinutes(1) : TimeSpan.Zero;
        _app.Clock.Advance(ahead);
        try
        {
            await SendAsync();
        }
        finally
        {
            _app.Clock.Advance(-ahead);
        }
    }

    /// <summary>An organization on a PayPal plan with a trial, its owner's email, and its checkout (as the reconciliation tests make them).</summary>
    private async Task<(int TenantId, string OwnerEmail, CheckoutSession Session)> OrganizationAsync(int trialDays = 30) => await UseAsync(async sp =>
    {
        var plans = sp.GetRequiredService<PlanService>();
        var planId = (await plans.CreateAsync($"al-{Guid.NewGuid():N}"[..12], new("Alert plan", null, null, null, trialDays, true), BillingCycle.Monthly, 15m, "USD", "test")).PlanId!.Value;
        await sp.GetRequiredService<PlanPublisher>().PublishAsync(planId, "test");
        var price = (await plans.FindAsync(planId))!.Prices[0];
        var owner = $"o-{Guid.NewGuid():N}"[..12] + "@alerts.test";
        var tenant = (await sp.GetRequiredService<OrganizationService>().CreateAsync(new NewOrganization($"Alert Org {Guid.NewGuid():N}"[..20], $"al-{Guid.NewGuid():N}"[..14],
            "UTC", null, null, "Abena", "Owner", owner, null, planId, price.Id, BillingMode.PayPal, trialDays), "test", "https://localhost")).TenantId!.Value;
        var started = await sp.GetRequiredService<CheckoutService>().StartAsync(new StartCheckout(CheckoutPurpose.Convert, tenant, price.Id, "https://localhost/Billing/Return", "https://localhost/Billing", Guid.NewGuid().ToString(), "test"));
        return (tenant, owner, started.Session!);
    });

    private async Task<(string Email, string UserId)> ProviderUserAsync(ProviderRole role)
    {
        var email = $"pa-{Guid.NewGuid():N}"[..13] + "@alerts.test";
        await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, role);
        return (email, (await _app.FindUserAsync(email)).Id);
    }

    private IEnumerable<string> SubjectsTo(string email) => _app.Emails.To(email).Select(e => e.Subject);

    [Fact]
    public async Task A_Failed_Payment_Then_A_Suspension_Reach_The_Owner_And_Inprem_Once_Each()
    {
        var (billingEmail, _) = await ProviderUserAsync(ProviderRole.Billing);
        var (tenant, owner, session) = await OrganizationAsync();
        var id = session.ExternalSubscriptionId!;
        _app.PayPal.SetState(id, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await UseAsync(sp => sp.GetRequiredService<BillingReconciliation>().CheckoutsAsync());

        // PayPal could not collect: the daily check finds it (as a webhook would).
        _app.PayPal.Subscriptions[id] = _app.PayPal.Subscriptions[id] with { OutstandingBalance = 15m, FailedPaymentsCount = 1 };
        await DailyAsync();
        await DailyAsync(); // nothing new the second time
        await SendAsync();
        Assert.Single(SubjectsTo(owner), s => s.StartsWith("PayPal could not collect the payment for"));
        Assert.Single(SubjectsTo(billingEmail), s => s.StartsWith("[Inprem billing]") && s.EndsWith(": a payment failed"));

        // Then PayPal suspends it.
        _app.PayPal.Subscriptions[id] = _app.PayPal.Subscriptions[id] with { State = PayPalSubscriptionState.Suspended, OutstandingBalance = 30m, FailedPaymentsCount = 3 };
        await DailyAsync();
        await SendAsync();
        Assert.Single(SubjectsTo(owner), s => s.EndsWith("subscription is suspended"));
        Assert.Single(SubjectsTo(billingEmail), s => s.EndsWith(": the subscription was suspended"));

        await using var db = _app.CreatePlatformDbContext();
        Assert.Equal(SubscriptionStatus.Suspended, (await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenant)).Status);
    }

    [Fact]
    public async Task A_Daily_Summary_User_Gets_One_Summary_Not_Each_Problem()
    {
        var summaryEmail = $"ds-{Guid.NewGuid():N}"[..13] + "@alerts.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, summaryEmail, ProviderRole.Billing);
        var summaryUserId = (await _app.FindUserAsync(summaryEmail)).Id;
        // The choice is made on "My alerts", as the person would.
        using (var client = _app.CreateClient())
        {
            await SignInAsync(client, summaryEmail, key);
            var saved = await PostAsync(client, "/Provider/MyAlerts", ("NewSignups", "true"), ("BillingProblems", "DailySummary"));
            Assert.Equal("/Provider/MyAlerts", Location(saved));
            Assert.Contains("saved", (await GetDocumentAsync(client, "/Provider/MyAlerts")).QuerySelector("[data-testid=message]")!.TextContent);
        }
        await using (var db = _app.CreatePlatformDbContext())
        {
            Assert.Equal(AlertDelivery.DailySummary, (await db.ProviderAlertPreferences.AsNoTracking().SingleAsync(p => p.UserId == summaryUserId)).BillingProblems);
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "provider.alerts-changed" && a.Target == summaryUserId));
        }

        var (_, _, session) = await OrganizationAsync();
        var id = session.ExternalSubscriptionId!;
        _app.PayPal.SetState(id, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await UseAsync(sp => sp.GetRequiredService<BillingReconciliation>().CheckoutsAsync());
        _app.PayPal.Subscriptions[id] = _app.PayPal.Subscriptions[id] with { OutstandingBalance = 15m, FailedPaymentsCount = 1 };

        await DailyAsync(); // finds the problem and, at its end, sends the day's summary
        await SendAsync();

        Assert.DoesNotContain(SubjectsTo(summaryEmail), s => s.EndsWith(": a payment failed"));
        Assert.Single(SubjectsTo(summaryEmail), s => s.StartsWith("[Inprem billing] Daily summary:"));
    }

    [Fact]
    public async Task The_Trial_Ending_Reminders_Come_7_Days_And_1_Day_Before_Once_Each()
    {
        var (_, owner, _) = await OrganizationAsync(trialDays: 7);

        await DailyAsync();
        await DailyAsync();
        await SendAsync();
        Assert.Single(SubjectsTo(owner), s => s.EndsWith("ends in 7 days"));

        _app.Clock.Advance(TimeSpan.FromDays(6) + TimeSpan.FromHours(1));
        try
        {
            await DailyAsync();
            await DailyAsync();
            await SendAsync();
        }
        finally
        {
            _app.Clock.Advance(-(TimeSpan.FromDays(6) + TimeSpan.FromHours(1)));
        }
        Assert.Single(SubjectsTo(owner), s => s.EndsWith("ends tomorrow"));
        Assert.Contains("/Billing", _app.Emails.To(owner).Single(e => e.Subject.EndsWith("ends tomorrow")).HtmlBody);
    }

    [Fact]
    public async Task A_New_Invoice_Texts_The_Owner_And_An_Overdue_One_Is_Told_Once()
    {
        var (tenant, owner, _) = await OrganizationAsync();
        var phone = "+1202555" + Random.Shared.Next(1000, 9999);
        await UseAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(owner))!;
            await users.SetPhoneNumberAsync(user, phone);
            return await sp.GetRequiredService<NotificationService>().RecordConsentAsync(phone, tenant, user.Id, ConsentSources.WebsiteSignup, ConsentWording.OwnerAlerts, "test");
        });

        var created = await UseAsync(sp => sp.GetRequiredService<InvoiceService>().CreateAndSendAsync(
            new NewInvoiceInput(tenant, InvoiceSource.Manual, new[] { new InvoiceLineInput("Setup", 1, 40m) }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), null), "test"));
        Assert.True(created.Succeeded, created.Error);
        await SendAllAsync();
        Assert.Contains(_app.Sms.To(phone), t => t.Body.Contains("40.00 USD is due"));
        Assert.Single(SubjectsTo(owner), s => s.StartsWith("Invoice INV-")); // the invoice's own email, not a second one

        await using (var db = _app.CreatePlatformDbContext())
            await db.Invoices.Where(i => i.Id == created.InvoiceId).ExecuteUpdateAsync(u => u.SetProperty(i => i.DueDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2))));
        await DailyAsync();
        await DailyAsync();
        await SendAllAsync();
        Assert.Single(SubjectsTo(owner), s => s.EndsWith("is overdue"));
        Assert.Single(_app.Sms.To(phone), t => t.Body.Contains("not paid yet"));
    }

    [Fact]
    public async Task A_Webhook_That_Fails_Verification_Alerts_Inprems_Owners_Once_An_Hour()
    {
        var (ownerEmail, _) = await ProviderUserAsync(ProviderRole.Owner);
        var verify = _app.PayPal.Verify;
        _app.PayPal.Verify = _ => false;
        try
        {
            using var client = _app.CreateClient();
            for (var i = 0; i < 2; i++)
            {
                var body = $$$"""{"id":"WH-{{{Guid.NewGuid():N}}}","event_type":"BILLING.SUBSCRIPTION.ACTIVATED","resource":{"id":"I-X"}}""";
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/webhooks/paypal", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
            }
        }
        finally
        {
            _app.PayPal.Verify = verify;
        }
        await SendAsync();
        Assert.Single(SubjectsTo(ownerEmail), s => s == "[Inprem] System problem: a PayPal webhook failed verification");
    }
}
