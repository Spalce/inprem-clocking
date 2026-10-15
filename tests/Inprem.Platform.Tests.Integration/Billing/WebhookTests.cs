using System.Net;
using System.Text;
using Hangfire;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 2 step 2.5 (P2-3): <c>/webhooks/paypal</c> verifies, stores once and queues; the job
/// re-reads PayPal and applies its current state (so duplicates and out-of-order events end in the
/// same place); payments once per sale; failures kept and replayable from the console.
/// </summary>
public class WebhookTests : IClassFixture<ProviderFixture>
{
    private const string GoodSignature = "good-signature";
    private readonly ProviderFixture _app;

    public WebhookTests(ProviderFixture app)
    {
        _app = app;
        _app.PayPal.Verify = m => m.Headers.TryGetValue("PAYPAL-TRANSMISSION-SIG", out var sig) && sig == GoodSignature;
    }

    // ------------------------------------------------------------------ setup

    private async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static string Event(string type, string resourceType, string resourceJson, string? id = null) =>
        $$"""{"id":"{{id ?? "WH-" + Guid.NewGuid().ToString("N")}}","event_type":"{{type}}","resource_type":"{{resourceType}}","create_time":"2026-10-15T10:00:00Z","resource":{{resourceJson}}}""";

    private static string SubscriptionEvent(string type, string subscriptionId, string? id = null) =>
        Event(type, "subscription", $$$"""{"id":"{{{subscriptionId}}}","status":"WHATEVER","subscriber":{"email_address":"payer@buyer.test"}}""", id);

    private static string SaleEvent(string type, string saleId, string subscriptionId, decimal amount, string? id = null) =>
        Event(type, "sale", $$$"""{"id":"{{{saleId}}}","billing_agreement_id":"{{{subscriptionId}}}","amount":{"total":"{{{amount:0.00}}}","currency":"USD"},"state":"completed","create_time":"2026-10-15T10:00:00Z"}""", id);

    private async Task<HttpResponseMessage> PostAsync(string body, string signature = GoodSignature)
    {
        using var client = _app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/paypal") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in new[] { ("PAYPAL-AUTH-ALGO", "SHA256withRSA"), ("PAYPAL-CERT-URL", "https://api.sandbox.paypal.com/v1/notifications/certs/CERT"),
                     ("PAYPAL-TRANSMISSION-ID", Guid.NewGuid().ToString()), ("PAYPAL-TRANSMISSION-SIG", signature), ("PAYPAL-TRANSMISSION-TIME", "2026-10-15T10:00:00Z") })
            request.Headers.Add(name, value);
        return await client.SendAsync(request);
    }

    /// <summary>Stores the event through the endpoint and runs its job here (no job server in tests).</summary>
    private async Task<BillingEvent> DeliverAsync(string body)
    {
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(body)).StatusCode);
        var id = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
        await UseAsync(async sp => { await sp.GetRequiredService<BillingEventProcessor>().ProcessAsync(id); return true; });
        return (await EventAsync(id))!;
    }

    private async Task<BillingEvent?> EventAsync(string id)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.BillingEvents.AsNoTracking().SingleOrDefaultAsync(e => e.ExternalEventId == id);
    }

    private async Task<Subscription> SubscriptionAsync(int tenantId)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
    }

    private async Task<List<string>> AuditAsync(int tenantId, string prefix)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.Where(a => a.TenantId == tenantId && a.Action.StartsWith(prefix)).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
    }

    /// <summary>An organization with a PayPal checkout started (PayPal says APPROVAL_PENDING).</summary>
    private async Task<(int TenantId, CheckoutSession Session, PlanPrice Price)> CheckoutAsync() => await UseAsync(async sp =>
    {
        var plans = sp.GetRequiredService<PlanService>();
        var planId = (await plans.CreateAsync($"wh-{Guid.NewGuid():N}"[..12], new("Webhook plan", null, null, null, 7, true), BillingCycle.Monthly, 25m, "USD", "test")).PlanId!.Value;
        await sp.GetRequiredService<PlanPublisher>().PublishAsync(planId, "test");
        var price = (await plans.FindAsync(planId))!.Prices[0];
        var created = await sp.GetRequiredService<OrganizationService>().CreateAsync(new NewOrganization($"Webhook Org {Guid.NewGuid():N}"[..21], $"wh-{Guid.NewGuid():N}"[..14],
            "UTC", null, null, "W", "Owner", $"w-{Guid.NewGuid():N}"[..10] + "@webhook.test", null, planId, price.Id, BillingMode.PayPal, 7), "test", "https://localhost");
        var tenant = created.TenantId!.Value;
        var started = await sp.GetRequiredService<CheckoutService>().StartAsync(new StartCheckout(CheckoutPurpose.Convert, tenant, price.Id, "https://localhost/Billing/Return", "https://localhost/Billing", Guid.NewGuid().ToString(), "test"));
        Assert.True(started.Succeeded, started.Error);
        return (tenant, started.Session!, price);
    });

    /// <summary>An organization whose PayPal subscription is active (activated by the ACTIVATED webhook).</summary>
    private async Task<(int TenantId, string SubscriptionId, PlanPrice Price)> ActiveAsync()
    {
        var (tenant, session, price) = await CheckoutAsync();
        _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", session.ExternalSubscriptionId!));
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(tenant)).Status);
        return (tenant, session.ExternalSubscriptionId!, price);
    }

    private void PayPalSays(string subscriptionId, Func<PayPalSubscription, PayPalSubscription> change) =>
        _app.PayPal.Subscriptions[subscriptionId] = change(_app.PayPal.Subscriptions[subscriptionId]);

    // ---------------------------------------------------------------- endpoint

    [Fact]
    public async Task Unsigned_Or_Tampered_Events_Are_Rejected_And_Not_Stored()
    {
        var id = "WH-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", "I-X", id), signature: "tampered")).StatusCode);
        Assert.Null(await EventAsync(id));

        using var client = _app.CreateClient();
        var unsigned = await client.PostAsync("/webhooks/paypal", new StringContent(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", "I-X", id), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);
        Assert.Null(await EventAsync(id));

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync("""{"no":"id"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync("not json")).StatusCode);
    }

    [Fact]
    public async Task A_Valid_Event_Is_Stored_Once_Queued_And_Answered_At_Once()
    {
        var id = "WH-" + Guid.NewGuid().ToString("N");
        var body = SubscriptionEvent("BILLING.SUBSCRIPTION.UPDATED", "I-UNKNOWN", id);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(body)).StatusCode); // duplicate delivery: 200, nothing new

        await using (var db = _app.CreatePlatformDbContext())
        {
            var stored = await db.BillingEvents.AsNoTracking().Where(e => e.ExternalEventId == id).ToListAsync();
            Assert.Single(stored);
            Assert.Equal(("BILLING.SUBSCRIPTION.UPDATED", "subscription", "I-UNKNOWN", BillingEventStatus.Received), (stored[0].EventType, stored[0].ResourceType, stored[0].ResourceId, stored[0].Status));
            Assert.Equal(body, stored[0].RawJson); // kept as received, for replay
        }

        var queued = _app.Services.GetRequiredService<JobStorage>().GetMonitoringApi().EnqueuedJobs("default", 0, 1000)
            .Count(j => j.Value.Job?.Args.FirstOrDefault() as string == id);
        Assert.Equal(1, queued);
    }

    // -------------------------------------------------------------- processing

    [Fact]
    public async Task Activated_Webhook_Activates_Through_The_Checkout_Exactly_Once()
    {
        var (tenant, session, _) = await CheckoutAsync();
        var subscriptionId = session.ExternalSubscriptionId!;

        // The ACTIVATED event can arrive before PayPal's API says ACTIVE (it re-reads): nothing yet.
        var early = await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", subscriptionId));
        Assert.Equal(BillingEventStatus.Processed, early.Status);
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync(tenant)).Status);

        _app.PayPal.SetState(subscriptionId, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", subscriptionId)); // PayPal redelivers
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", subscriptionId));
        await UseAsync(sp => sp.GetRequiredService<CheckoutService>().ConfirmAsync(session.Id, CheckoutSource.Confirm)); // and the buyer's return

        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(tenant)).Status);
        Assert.Equal(new[] { "subscription.activated" }, await AuditAsync(tenant, "subscription."));
    }

    [Fact]
    public async Task Payment_Failed_Then_Sale_Completed_Moves_PastDue_And_Back_And_Records_The_Payment_Once()
    {
        var (tenant, subscriptionId, _) = await ActiveAsync();

        PayPalSays(subscriptionId, s => s with { OutstandingBalance = 25m, FailedPaymentsCount = 1 });
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.PAYMENT.FAILED", subscriptionId));
        var pastDue = await SubscriptionAsync(tenant);
        Assert.Equal((SubscriptionStatus.PastDue, 25m), (pastDue.Status, pastDue.OutstandingBalance));
        Assert.True(pastDue.HasFullAccess(DateTime.UtcNow)); // PayPal is retrying

        var next = DateTime.UtcNow.AddMonths(2);
        PayPalSays(subscriptionId, s => s with { OutstandingBalance = 0m, FailedPaymentsCount = 0, LastPaymentAmount = 25m, LastPaymentCurrency = "USD", LastPaymentUtc = DateTime.UtcNow, NextBillingUtc = next });
        var sale = "SALE-" + Guid.NewGuid().ToString("N")[..10];
        await DeliverAsync(SaleEvent("PAYMENT.SALE.COMPLETED", sale, subscriptionId, 25m));
        await DeliverAsync(SaleEvent("PAYMENT.SALE.COMPLETED", sale, subscriptionId, 25m)); // the same sale again (new event id)

        var recovered = await SubscriptionAsync(tenant);
        Assert.Equal((SubscriptionStatus.Active, 0m, next.Date), (recovered.Status, recovered.OutstandingBalance, recovered.CurrentPeriodEndUtc!.Value.Date));
        await using (var db = _app.CreatePlatformDbContext())
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ExternalId == sale);
            Assert.Equal((tenant, 25m, "USD", PaymentStatus.Completed), (payment.TenantId, payment.Amount, payment.Currency, payment.Status));
        }

        // Refunded (part), then reversed.
        await DeliverAsync(Event("PAYMENT.SALE.REFUNDED", "refund", $$$"""{"id":"REF-1","sale_id":"{{{sale}}}","amount":{"total":"10.00","currency":"USD"}}"""));
        await using (var db = _app.CreatePlatformDbContext())
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ExternalId == sale);
            Assert.Equal((PaymentStatus.Refunded, 10m), (payment.Status, payment.RefundedAmount));
        }
        Assert.Equal(new[] { "subscription.activated", "subscription.past-due", "subscription.payment-recovered" }, await AuditAsync(tenant, "subscription."));
        Assert.Equal(new[] { "payment.received", "payment.refunded" }, await AuditAsync(tenant, "payment."));
    }

    [Fact]
    public async Task Out_Of_Order_Events_Follow_PayPals_Current_State()
    {
        var (tenant, subscriptionId, _) = await ActiveAsync();

        // A late SUSPENDED event arrives, but PayPal says ACTIVE now: nothing happens.
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.SUSPENDED", subscriptionId));
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(tenant)).Status);

        // Really suspended: read-only.
        PayPalSays(subscriptionId, s => s with { State = PayPalSubscriptionState.Suspended, OutstandingBalance = 25m });
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.SUSPENDED", subscriptionId));
        var suspended = await SubscriptionAsync(tenant);
        Assert.Equal(SubscriptionStatus.Suspended, suspended.Status);
        Assert.False(suspended.HasFullAccess(DateTime.UtcNow));

        // Paid and reactivated in PayPal: active again (an UPDATED event is enough, whatever arrives first).
        PayPalSays(subscriptionId, s => s with { State = PayPalSubscriptionState.Active, OutstandingBalance = 0m, FailedPaymentsCount = 0 });
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.UPDATED", subscriptionId));
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(tenant)).Status);

        // Cancelled, then a stale ACTIVATED arrives: PayPal still says CANCELLED, so it stays cancelled.
        PayPalSays(subscriptionId, s => s with { State = PayPalSubscriptionState.Cancelled });
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.CANCELLED", subscriptionId));
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", subscriptionId));
        var cancelled = await SubscriptionAsync(tenant);
        Assert.Equal((SubscriptionStatus.Canceled, true), (cancelled.Status, cancelled.CancelAtPeriodEnd));

        PayPalSays(subscriptionId, s => s with { State = PayPalSubscriptionState.Expired });
        await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.EXPIRED", subscriptionId));
        Assert.Equal(SubscriptionStatus.Expired, (await SubscriptionAsync(tenant)).Status);

        Assert.Equal(new[] { "subscription.activated", "subscription.suspended", "subscription.reactivated", "subscription.canceled", "subscription.expired" },
            await AuditAsync(tenant, "subscription."));
    }

    [Fact]
    public async Task Events_For_Unknown_Subscriptions_And_Other_Types_Are_Processed_Without_Changes()
    {
        Assert.Equal(BillingEventStatus.Processed, (await DeliverAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.UPDATED", "I-NOT-OURS"))).Status);
        Assert.Equal(BillingEventStatus.Processed, (await DeliverAsync(Event("CUSTOMER.DISPUTE.CREATED", "dispute", """{"id":"PP-D-1"}"""))).Status);
        Assert.Equal(BillingEventStatus.Processed, (await DeliverAsync(SaleEvent("PAYMENT.SALE.COMPLETED", "SALE-X", "I-NOT-OURS", 5m))).Status);
    }

    [Fact]
    public async Task A_Failure_Is_Kept_On_The_Event_And_Replay_From_The_Console_Finishes_It()
    {
        var (tenant, session, _) = await CheckoutAsync();
        var subscriptionId = session.ExternalSubscriptionId!;
        _app.PayPal.SetState(subscriptionId, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));

        // The ACTIVATED event is stored, then processing fails: PayPal's API is down.
        var id = "WH-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(SubscriptionEvent("BILLING.SUBSCRIPTION.ACTIVATED", subscriptionId, id))).StatusCode);
        _app.PayPal.FailNextCalls = 1;
        try
        {
            await Assert.ThrowsAsync<PaymentProviderException>(() => UseAsync(async sp => { await sp.GetRequiredService<BillingEventProcessor>().ProcessAsync(id); return true; }));
        }
        finally
        {
            _app.PayPal.FailNextCalls = 0;
        }
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync(tenant)).Status);
        var failed = (await EventAsync(id))!;
        Assert.Equal((BillingEventStatus.Failed, 1), (failed.Status, failed.Attempts));
        Assert.Contains("pretend failure", failed.Error);

        // The console shows it; Billing replays it from the event's page.
        var email = $"bill-{Guid.NewGuid():N}"[..14] + "@webhook.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Billing);
        using var billing = _app.CreateClient();
        await SignInAsync(billing, email, key);
        var list = await GetDocumentAsync(billing, "/Provider/Webhooks?Status=Failed");
        Assert.Contains(subscriptionId, list.QuerySelector("[data-testid=events]")!.TextContent);
        var page = await GetDocumentAsync(billing, $"/Provider/Webhooks/Details/{id}");
        Assert.Contains("pretend failure", page.QuerySelector("[data-testid=error]")!.TextContent);
        Assert.DoesNotContain("payer@buyer.test", page.Body!.InnerHtml); // the payer's details are not shown

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(billing, $"/Provider/Webhooks/Details/{id}", action => action.EndsWith("handler=Replay", StringComparison.Ordinal))).StatusCode);
        await using (var db = _app.CreatePlatformDbContext())
        {
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "billing-event.replayed" && a.Target == id));
        }
        await UseAsync(async sp => { await sp.GetRequiredService<BillingEventProcessor>().ProcessAsync(id); return true; }); // what the queued job does
        var done = (await EventAsync(id))!;
        Assert.Equal((BillingEventStatus.Processed, 2, null), (done.Status, done.Attempts, done.Error));
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(tenant)).Status);

        // Support cannot see webhook events.
        var supportEmail = $"sup-{Guid.NewGuid():N}"[..14] + "@webhook.test";
        var supportKey = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, supportEmail, ProviderRole.Support);
        using var support = _app.CreateClient();
        await SignInAsync(support, supportEmail, supportKey);
        Assert.EndsWith("/Provider/AccessDenied", Location(await support.GetAsync($"/Provider/Webhooks/Details/{id}")));
    }
}
