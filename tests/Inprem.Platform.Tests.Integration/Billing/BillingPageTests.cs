using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 2 step 2.8 (P2-1 with the pretend PayPal): the owner's Billing page (subscribe, return,
/// cancel at PayPal, cancel the subscription, payments), owners only; and the console's
/// subscription actions (extend trial, free access, sync, cancel), Owners and Billing only.
/// </summary>
public class BillingPageTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public BillingPageTests(ProviderFixture app) => _app = app;

    private int Tenant => _app.Data.Beta.Id;

    /// <summary>Beta on a 30-day PayPal trial (10 days left) of a published plan; returns the plan.</summary>
    private async Task<Plan> TrialAsync(Action<Subscription, PlanPrice, DateTime>? shape = null)
    {
        using var scope = _app.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var planId = (await plans.CreateAsync($"bp-{Guid.NewGuid():N}"[..12], new("Billing page plan", "For the tests", null, null, 30, true), BillingCycle.Monthly, 12m, "USD", "test")).PlanId!.Value;
        await plans.AddPriceAsync(planId, BillingCycle.Yearly, 120m, "USD", "test");
        Assert.True((await scope.ServiceProvider.GetRequiredService<PlanPublisher>().PublishAsync(planId, "test")).Succeeded);
        var plan = (await plans.FindAsync(planId))!;

        await using var db = _app.CreatePlatformDbContext();
        await db.Subscriptions.Where(s => s.TenantId == Tenant).ExecuteDeleteAsync();
        var now = _app.Clock.UtcNow;
        var subscription = Subscription.Start(Tenant, plan, plan.Prices[0], BillingMode.PayPal, 30, now.AddDays(-20));
        shape?.Invoke(subscription, plan.Prices[0], now);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return plan;
    }

    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);
        return client;
    }

    private Task<HttpClient> OwnerAsync() => SignedInAsync(_app.Data.Beta.Owner!.Email);

    private async Task<Subscription> SubscriptionAsync()
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == Tenant);
    }

    private static async Task<AngleSharp.Html.Dom.IHtmlDocument> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {response.Headers.Location}");
        return new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Presses the subscribe button for <paramref name="price"/> on the Billing page.</summary>
    private static async Task<HttpResponseMessage> SubscribeAsync(HttpClient client, PlanPrice price)
    {
        var page = await PageAsync(client, "/Billing");
        var form = page.QuerySelectorAll("form").First(f => f.QuerySelector($"input[name=priceId][value='{price.Id}']") != null);
        var fields = form.QuerySelectorAll("input[name]").ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value") ?? "");
        return await client.PostAsync("/Billing?handler=Subscribe", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task The_Owner_Subscribes_Through_PayPal_And_Becomes_Active_Only_When_PayPal_Confirms()
    {
        var plan = await TrialAsync();
        using var owner = await OwnerAsync();

        // Sidebar link and the page.
        Assert.NotNull((await PageAsync(owner, "/BackOffice")).QuerySelector("[data-testid=billing-link]"));
        var billing = await PageAsync(owner, "/Billing");
        Assert.Equal("Trialing", billing.QuerySelector("[data-testid=status]")!.TextContent.Trim());
        Assert.Contains("Billing page plan", billing.QuerySelector("[data-testid=plans]")!.TextContent);

        // Subscribe: off to PayPal's approve link.
        var toPayPal = await SubscribeAsync(owner, plan.Prices.Single(p => p.Cycle == BillingCycle.Monthly));
        Assert.Equal(HttpStatusCode.Redirect, toPayPal.StatusCode);
        Assert.StartsWith("https://www.sandbox.paypal.com/", toPayPal.Headers.Location!.ToString());
        CheckoutSession session;
        await using (var db = _app.CreatePlatformDbContext())
        {
            session = await db.CheckoutSessions.AsNoTracking().Where(c => c.TenantId == Tenant).OrderByDescending(c => c.CreatedUtc).FirstAsync();
        }
        Assert.Equal("https://localhost/Billing/Return?key=" + session.IdempotencyKey, session.ReturnUrl);

        // Back from PayPal before PayPal confirms: still confirming, not active.
        var back = await PageAsync(owner, $"/Billing/Return?key={session.IdempotencyKey}");
        Assert.Equal("AwaitingApproval", back.QuerySelector("[data-testid=return]")!.GetAttribute("data-status"));
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync()).Status);

        // PayPal confirms: the polling call sees it.
        _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        var poll = await owner.GetAsync($"/Billing/Return?handler=Status&key={session.IdempotencyKey}");
        var json = JsonDocument.Parse(await poll.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(("Active", true), (json.GetProperty("status").GetString(), json.GetProperty("final").GetBoolean()));
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync()).Status);

        var after = await PageAsync(owner, "/Billing");
        Assert.Equal("Active", after.QuerySelector("[data-testid=status]")!.TextContent.Trim());
        Assert.NotNull(after.QuerySelector("[data-testid=cancel]"));
        Assert.Contains("Change plan", after.QuerySelector("[data-testid=plans]")!.TextContent);

        // The plan's fallback return page finds the same checkout.
        var fallback = await owner.GetAsync($"/Billing/PayPalReturn?subscription_id={session.ExternalSubscriptionId}&ba_token=BA-1");
        Assert.Equal($"/Billing/Return?key={session.IdempotencyKey}", fallback.Headers.Location!.ToString());

        // Cancel the subscription (own page, reason).
        Assert.Contains("Why are you cancelling", (await PageAsync(owner, "/Billing/Cancel")).Body!.TextContent);
        var cancel = await PostFormAsync(owner, "/Billing/Cancel", a => a.Length == 0 || a.Contains("/Billing/Cancel", StringComparison.Ordinal), ("Reason", "Closing for the summer"));
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        var cancelled = await SubscriptionAsync();
        Assert.Equal((SubscriptionStatus.Canceled, true), (cancelled.Status, cancelled.CancelAtPeriodEnd));
    }

    [Fact]
    public async Task Cancelling_At_PayPal_Ends_The_Checkout_And_Charges_Nothing()
    {
        var plan = await TrialAsync();
        using var owner = await OwnerAsync();
        await SubscribeAsync(owner, plan.Prices[0]);
        CheckoutSession session;
        await using (var db = _app.CreatePlatformDbContext())
        {
            session = await db.CheckoutSessions.AsNoTracking().Where(c => c.TenantId == Tenant).OrderByDescending(c => c.CreatedUtc).FirstAsync();
        }

        var back = await owner.GetAsync($"/Billing/Cancelled?key={session.IdempotencyKey}");
        Assert.Equal("/Billing", back.Headers.Location!.ToString());
        Assert.Contains("Nothing was charged", (await PageAsync(owner, "/Billing")).QuerySelector("[data-testid=error]")!.TextContent);
        await using (var db = _app.CreatePlatformDbContext())
        {
            Assert.Equal(CheckoutStatus.Cancelled, (await db.CheckoutSessions.AsNoTracking().SingleAsync(c => c.Id == session.Id)).Status);
        }
    }

    [Fact]
    public async Task Only_The_Owner_Has_Billing_And_A_Read_Only_Owner_Can_Still_Pay()
    {
        var plan = await TrialAsync((s, price, now) =>
        {
            s.Activate($"I-{Guid.NewGuid():N}"[..14], price, now.AddDays(-20), now.AddDays(10), now.AddDays(-20));
            s.Suspend(now.AddDays(-1));
        });

        using (var admin = await SignedInAsync(_app.Data.Beta.Admin!.Email))
        {
            Assert.Null((await PageAsync(admin, "/BackOffice")).QuerySelector("[data-testid=billing-link]"));
            Assert.Contains("AccessDenied", (await admin.GetAsync("/Billing")).Headers.Location?.ToString() ?? "");
        }

        using var owner = await OwnerAsync();
        Assert.NotNull((await PageAsync(owner, "/Account/ReadOnly")).QuerySelector("[data-testid=billing]"));
        Assert.Contains("Subscribe", (await PageAsync(owner, "/Billing")).QuerySelector("[data-testid=plans]")!.TextContent);
        var toPayPal = await SubscribeAsync(owner, plan.Prices[0]); // Billing is never blocked by read-only
        Assert.StartsWith("https://www.sandbox.paypal.com/", toPayPal.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Console_Extends_Trials_Grants_Free_Access_Syncs_And_Cancels()
    {
        await TrialAsync((s, _, now) => s.ExtendTrial(now.AddMinutes(1), now.AddMinutes(-1)));
        _app.Clock.Advance(TimeSpan.FromMinutes(5)); // the trial is over now
        try
        {
            var email = $"bill-{Guid.NewGuid():N}"[..14] + "@billingpage.test";
            var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Billing);
            using var billing = _app.CreateClient();
            await SignInAsync(billing, email, key);

            var tab = await GetDocumentAsync(billing, $"/Provider/Tenants/Details/{Tenant}?tab=subscription");
            Assert.Contains("read-only", tab.QuerySelector("[data-testid=subscription]")!.TextContent);

            // Extend the trial: full access again.
            var newEnd = DateTime.UtcNow.Date.AddDays(10);
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, $"/Provider/Tenants/ExtendTrial/{Tenant}", ("TrialEnds", newEnd.ToString("yyyy-MM-dd")))).StatusCode);
            Assert.Equal(newEnd, (await SubscriptionAsync()).TrialEndsUtc!.Value.Date);

            // Free access: a reason is required; then shown on the tab; then removed.
            Assert.Contains("reason", (await BodyAsync(await PostAsync(billing, $"/Provider/Tenants/FreeAccess/{Tenant}", ("Until", newEnd.AddDays(20).ToString("yyyy-MM-dd")), ("Reason", " ")))), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, $"/Provider/Tenants/FreeAccess/{Tenant}", ("Until", newEnd.AddDays(20).ToString("yyyy-MM-dd")), ("Reason", "Goodwill after an outage"))).StatusCode);
            Assert.Equal(newEnd.AddDays(20).ToString("yyyy-MM-dd"), (await GetDocumentAsync(billing, $"/Provider/Tenants/Details/{Tenant}?tab=subscription")).QuerySelector("[data-testid=free-access]")!.TextContent.Trim());
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, $"/Provider/Tenants/FreeAccess/{Tenant}", ("Remove", "true"), ("Reason", "Over"))).StatusCode);
            Assert.Null((await SubscriptionAsync()).FreeAccessUntilUtc);

            // Make it a paying subscription, then sync and cancel from the console.
            var subscriptionId = await ActivateViaCheckoutAsync();
            _app.PayPal.Subscriptions[subscriptionId] = _app.PayPal.Subscriptions[subscriptionId] with { OutstandingBalance = 12m, FailedPaymentsCount = 1 };
            Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(billing, $"/Provider/Tenants/Details/{Tenant}?tab=subscription", a => a.Contains("handler=Sync", StringComparison.Ordinal))).StatusCode);
            Assert.Equal(SubscriptionStatus.PastDue, (await SubscriptionAsync()).Status);
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, $"/Provider/Tenants/CancelSubscription/{Tenant}", ("Reason", "Asked by the owner on the phone"))).StatusCode);
            Assert.Equal(SubscriptionStatus.Canceled, (await SubscriptionAsync()).Status);

            await using (var db = _app.CreatePlatformDbContext())
            {
                var actions = await db.AuditEntries.Where(a => a.TenantId == Tenant && a.Actor.StartsWith("provider:")).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
                Assert.Equal(new[] { "subscription.trial-extended", "subscription.free-access-granted", "subscription.free-access-removed", "subscription.past-due", "subscription.canceled" }, actions);
            }

            // The console Billing list shows it; Support has no billing.
            Assert.Contains("Beta", (await GetDocumentAsync(billing, "/Provider/Billing?Status=Canceled")).QuerySelector("[data-testid=subscriptions]")!.TextContent);
            var supportEmail = $"sup-{Guid.NewGuid():N}"[..14] + "@billingpage.test";
            var supportKey = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, supportEmail, ProviderRole.Support);
            using var support = _app.CreateClient();
            await SignInAsync(support, supportEmail, supportKey);
            Assert.EndsWith("/Provider/AccessDenied", Location(await support.GetAsync($"/Provider/Tenants/FreeAccess/{Tenant}")));
            Assert.Null((await GetDocumentAsync(support, $"/Provider/Tenants/Details/{Tenant}?tab=subscription")).QuerySelector("[data-testid=sync]"));
        }
        finally
        {
            _app.Clock.Advance(-TimeSpan.FromMinutes(5));
        }
    }

    private async Task<string> ActivateViaCheckoutAsync()
    {
        using var scope = _app.Services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var priceId = (await SubscriptionAsync()).PlanPriceId!.Value;
        var started = await checkout.StartAsync(new StartCheckout(CheckoutPurpose.Convert, Tenant, priceId, "https://localhost/Billing/Return", "https://localhost/Billing", Guid.NewGuid().ToString(), "test"));
        Assert.True(started.Succeeded, started.Error);
        _app.PayPal.SetState(started.Session!.ExternalSubscriptionId!, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await checkout.ConfirmAsync(started.Session.Id, CheckoutSource.Confirm);
        return started.Session.ExternalSubscriptionId!;
    }
}
