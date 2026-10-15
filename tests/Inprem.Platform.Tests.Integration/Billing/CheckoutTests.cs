using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 2 step 2.4 (P2-2): checkout against the real database and a pretend PayPal. Never Active
/// from a redirect alone; activation exactly once whichever sources report it, in any order or
/// at the same moment; every verification mismatch fails the checkout; idempotency; plan change
/// and cancel.
/// </summary>
public class CheckoutTests : IClassFixture<ProviderFixture>
{
    private const string Return = "https://localhost/Billing/Return";
    private const string Cancel = "https://localhost/Billing";
    private readonly ProviderFixture _app;

    public CheckoutTests(ProviderFixture app) => _app = app;

    // ------------------------------------------------------------------ setup

    private async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<CheckoutResult> ConfirmAsync(Guid id, CheckoutSource source = CheckoutSource.Confirm) =>
        UseAsync(sp => sp.GetRequiredService<CheckoutService>().ConfirmAsync(id, source));

    /// <summary>A published plan (monthly and yearly) and an organization on a 7-day PayPal trial.</summary>
    private async Task<(int TenantId, PlanPrice Monthly, PlanPrice Yearly, string OwnerEmail)> TrialAsync()
    {
        return await UseAsync(async sp =>
        {
            var plans = sp.GetRequiredService<PlanService>();
            var planId = (await plans.CreateAsync($"co-{Guid.NewGuid():N}"[..12], new("Checkout plan", null, null, null, 7, true), BillingCycle.Monthly, 20m, "USD", "test")).PlanId!.Value;
            await plans.AddPriceAsync(planId, BillingCycle.Yearly, 200m, "USD", "test");
            Assert.True((await sp.GetRequiredService<PlanPublisher>().PublishAsync(planId, "test")).Succeeded);
            var plan = (await plans.FindAsync(planId))!;

            var owner = $"o-{Guid.NewGuid():N}"[..12] + "@checkout.test";
            var created = await sp.GetRequiredService<OrganizationService>().CreateAsync(new NewOrganization($"Checkout Org {Guid.NewGuid():N}"[..22], $"co-{Guid.NewGuid():N}"[..14],
                "UTC", null, null, "Olu", "Owner", owner, null, planId, plan.Prices.Single(p => p.Cycle == BillingCycle.Monthly).Id, BillingMode.PayPal, 7), "test", "https://localhost");
            Assert.True(created.Succeeded, created.Error);
            return (created.TenantId!.Value, plan.Prices.Single(p => p.Cycle == BillingCycle.Monthly), plan.Prices.Single(p => p.Cycle == BillingCycle.Yearly), owner);
        });
    }

    private Task<CheckoutResult> StartAsync(int tenantId, PlanPrice price, CheckoutPurpose purpose = CheckoutPurpose.Convert, string? key = null, string returnUrl = Return) =>
        UseAsync(sp => sp.GetRequiredService<CheckoutService>().StartAsync(new StartCheckout(purpose, tenantId, price.Id, returnUrl, Cancel, key ?? Guid.NewGuid().ToString(), "user:test")));

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

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task Start_Creates_A_PayPal_Subscription_From_Our_Catalogue_With_Our_Reference()
    {
        var (tenant, monthly, _, _) = await TrialAsync();
        var started = await StartAsync(tenant, monthly);

        Assert.True(started.Succeeded, started.Error);
        var session = started.Session!;
        Assert.Equal(CheckoutStatus.AwaitingApproval, session.Status);
        Assert.StartsWith("https://www.sandbox.paypal.com/", session.ApprovalUrl);
        var paypal = _app.PayPal.Subscriptions[session.ExternalSubscriptionId!];
        Assert.Equal((monthly.PayPalPlanId, session.Reference), (paypal.PlanId, paypal.CustomId)); // the plan id comes from our catalogue
        Assert.Equal(new[] { "checkout.started" }, await AuditAsync(tenant, "checkout."));
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync(tenant)).Status);
    }

    [Fact]
    public async Task A_Redirect_Alone_Never_Activates()
    {
        var (tenant, monthly, _, _) = await TrialAsync();
        var session = (await StartAsync(tenant, monthly)).Session!;

        // The buyer comes back, but PayPal still says APPROVAL_PENDING: nothing changes.
        Assert.Equal(CheckoutStatus.AwaitingApproval, (await ConfirmAsync(session.Id)).Session!.Status);
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync(tenant)).Status);

        // PayPal says APPROVED: still not Active; the organization waits for ACTIVATED.
        _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Approved);
        Assert.Equal(CheckoutStatus.Approved, (await ConfirmAsync(session.Id)).Session!.Status);
        var waiting = await SubscriptionAsync(tenant);
        Assert.Equal((SubscriptionStatus.PendingActivation, session.ExternalSubscriptionId), (waiting.Status, waiting.ExternalSubscriptionId));
        Assert.DoesNotContain("subscription.activated", await AuditAsync(tenant, "subscription."));
    }

    [Fact]
    public async Task Activation_Happens_Exactly_Once_From_Confirm_Webhook_And_Reconciliation_At_Once()
    {
        var (tenant, monthly, _, owner) = await TrialAsync();
        var session = (await StartAsync(tenant, monthly)).Session!;
        var next = DateTime.UtcNow.AddMonths(1);
        _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Active, next);

        var results = await Task.WhenAll(
            ConfirmAsync(session.Id, CheckoutSource.Confirm), ConfirmAsync(session.Id, CheckoutSource.Webhook), ConfirmAsync(session.Id, CheckoutSource.Reconciliation),
            ConfirmAsync(session.Id, CheckoutSource.Webhook), ConfirmAsync(session.Id, CheckoutSource.Confirm));

        Assert.All(results, r => Assert.Equal(CheckoutStatus.Active, r.Session!.Status));
        var s = await SubscriptionAsync(tenant);
        Assert.Equal((SubscriptionStatus.Active, BillingMode.PayPal, session.ExternalSubscriptionId, monthly.Id), (s.Status, s.Mode, s.ExternalSubscriptionId, s.PlanPriceId));
        Assert.Equal(next.Date, s.CurrentPeriodEndUtc!.Value.Date);
        Assert.Equal(new[] { "subscription.activated" }, await AuditAsync(tenant, "subscription."));
        Assert.Single(_app.Emails.To(owner), e => e.Subject == "Your subscription is active");

        // Later reports change nothing.
        await ConfirmAsync(session.Id, CheckoutSource.Reconciliation);
        Assert.Single(await AuditAsync(tenant, "subscription."));
    }

    [Fact]
    public async Task Activation_Is_Once_In_Any_Order_After_Approval()
    {
        var (tenant, monthly, _, owner) = await TrialAsync();
        var session = (await StartAsync(tenant, monthly)).Session!;
        _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Approved);
        await ConfirmAsync(session.Id, CheckoutSource.Confirm);
        _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await ConfirmAsync(session.Id, CheckoutSource.Reconciliation);
        await ConfirmAsync(session.Id, CheckoutSource.Webhook);
        await ConfirmAsync(session.Id, CheckoutSource.Confirm);

        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(tenant)).Status);
        Assert.Equal(new[] { "subscription.activated" }, await AuditAsync(tenant, "subscription."));
        Assert.Equal(new[] { "checkout.started", "checkout.approved", "checkout.active" }, await AuditAsync(tenant, "checkout."));
        Assert.Single(_app.Emails.To(owner), e => e.Subject == "Your subscription is active");
    }

    [Theory]
    [InlineData("custom_id")]
    [InlineData("plan")]
    [InlineData("other organization")]
    [InlineData("currency")]
    public async Task A_Mismatch_Fails_The_Checkout_And_Alerts_The_Provider(string what)
    {
        var (tenant, monthly, yearly, _) = await TrialAsync();
        var session = (await StartAsync(tenant, monthly)).Session!;
        var id = session.ExternalSubscriptionId!;
        _app.PayPal.SetState(id, PayPalSubscriptionState.Active);
        var s = _app.PayPal.Subscriptions[id];
        switch (what)
        {
            case "custom_id": _app.PayPal.Subscriptions[id] = s with { CustomId = "chk_someone_else" }; break;
            case "plan": _app.PayPal.Subscriptions[id] = s with { PlanId = yearly.PayPalPlanId }; break;
            case "currency": _app.PayPal.Subscriptions[id] = s with { LastPaymentAmount = 20m, LastPaymentCurrency = "EUR" }; break;
            case "other organization":
                // Another organization already has this PayPal subscription.
                var (other, otherMonthly, _, _) = await TrialAsync();
                await UseAsync<bool>(async sp =>
                {
                    var store = sp.GetRequiredService<ICheckoutStore>();
                    (await store.FindSubscriptionAsync(other))!.Activate(id, otherMonthly, null, null, DateTime.UtcNow);
                    await store.SaveChangesAsync();
                    return true;
                });
                break;
        }
        var alertsBefore = _app.Emails.Sent.Count(e => e.Subject.StartsWith("[Inprem billing]"));

        var result = await ConfirmAsync(session.Id, CheckoutSource.Webhook);

        Assert.False(result.Succeeded);
        Assert.Equal(CheckoutStatus.Failed, result.Session!.Status);
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync(tenant)).Status);
        Assert.Contains("checkout.failed", await AuditAsync(tenant, "checkout."));
        Assert.True(_app.Emails.Sent.Count(e => e.Subject.StartsWith("[Inprem billing]")) > alertsBefore || !await AnyBillingProviderAsync());
    }

    private async Task<bool> AnyBillingProviderAsync()
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.ProviderUsers.AnyAsync();
    }

    [Fact]
    public async Task The_Same_Request_Twice_Is_One_Checkout()
    {
        var (tenant, monthly, yearly, _) = await TrialAsync();
        var key = Guid.NewGuid().ToString();
        var first = await StartAsync(tenant, monthly, key: key);
        var calls = _app.PayPal.Calls.Count(c => c.StartsWith("CreateSubscription"));
        var again = await StartAsync(tenant, monthly, key: key);

        Assert.Equal(first.Session!.Id, again.Session!.Id);
        Assert.Equal(first.Session.ApprovalUrl, again.Session.ApprovalUrl);
        Assert.Equal(calls, _app.PayPal.Calls.Count(c => c.StartsWith("CreateSubscription")));
        Assert.False((await StartAsync(tenant, yearly, key: key)).Succeeded); // the key for something else
    }

    [Fact]
    public async Task Start_Refuses_What_It_Must()
    {
        var (tenant, monthly, _, _) = await TrialAsync();
        Assert.Contains("not allowed", (await StartAsync(tenant, monthly, returnUrl: "https://evil.test/r")).Error);
        Assert.Contains("Only an active PayPal subscription", (await StartAsync(tenant, monthly, CheckoutPurpose.ChangePlan)).Error);
        Assert.Contains("Only an expired", (await StartAsync(tenant, monthly, CheckoutPurpose.Resubscribe)).Error);

        // A price that is not in PayPal yet.
        var unpublished = await UseAsync(async sp =>
        {
            var plans = sp.GetRequiredService<PlanService>();
            var id = (await plans.CreateAsync($"un-{Guid.NewGuid():N}"[..12], new("Unpublished", null, null, null, 7, true), BillingCycle.Monthly, 5m, "USD", "test")).PlanId!.Value;
            return (await plans.FindAsync(id))!.Prices[0];
        });
        Assert.Contains("not available for online payment", (await StartAsync(tenant, unpublished)).Error);

        // Free organizations do not convert.
        var free = await UseAsync(async sp => (await sp.GetRequiredService<OrganizationService>().CreateAsync(new NewOrganization($"Free Org {Guid.NewGuid():N}"[..18], $"fr-{Guid.NewGuid():N}"[..14],
            "UTC", null, null, "F", "Owner", $"f-{Guid.NewGuid():N}"[..10] + "@checkout.test", null, null, null, BillingMode.Free, 0), "test", "https://localhost")).TenantId!.Value);
        Assert.Contains("Only an organization on a trial", (await StartAsync(free, monthly)).Error);

        // PayPal failing: the checkout is failed and says to try again; nothing changes.
        _app.PayPal.FailNextCalls = 1;
        var down = await StartAsync(tenant, monthly);
        Assert.Contains("try again", down.Error);
        Assert.Equal(CheckoutStatus.Failed, down.Session!.Status);
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync(tenant)).Status);
    }

    [Fact]
    public async Task Not_Approved_In_Time_Expires_And_The_Buyer_Can_Cancel()
    {
        var (tenant, monthly, _, _) = await TrialAsync();
        var late = (await StartAsync(tenant, monthly)).Session!;
        _app.Clock.Advance(CheckoutSession.Lifetime + TimeSpan.FromMinutes(1));
        try
        {
            Assert.Equal(CheckoutStatus.Expired, (await ConfirmAsync(late.Id, CheckoutSource.Reconciliation)).Session!.Status);
        }
        finally
        {
            _app.Clock.Advance(-(CheckoutSession.Lifetime + TimeSpan.FromMinutes(1)));
        }

        var cancelled = (await StartAsync(tenant, monthly)).Session!;
        var result = await UseAsync(sp => sp.GetRequiredService<CheckoutService>().CancelByBuyerAsync(cancelled.Id));
        Assert.Equal(CheckoutStatus.Cancelled, result.Session!.Status);
        _app.PayPal.SetState(cancelled.ExternalSubscriptionId!, PayPalSubscriptionState.Active); // too late: final
        Assert.Equal(CheckoutStatus.Cancelled, (await ConfirmAsync(cancelled.Id)).Session!.Status);
        Assert.Equal(SubscriptionStatus.Trialing, (await SubscriptionAsync(tenant)).Status);
    }

    [Fact]
    public async Task Change_Plan_Uses_A_Revision_And_Cancel_Keeps_Access_To_The_Period_End()
    {
        var (tenant, monthly, yearly, owner) = await TrialAsync();
        var session = (await StartAsync(tenant, monthly)).Session!;
        var subscriptionId = session.ExternalSubscriptionId!;
        _app.PayPal.SetState(subscriptionId, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await ConfirmAsync(session.Id);

        var change = await StartAsync(tenant, yearly, CheckoutPurpose.ChangePlan);
        Assert.True(change.Succeeded, change.Error);
        Assert.Equal(subscriptionId, change.Session!.ExternalSubscriptionId);
        Assert.Contains($"ReviseSubscription {subscriptionId} {yearly.PayPalPlanId}", _app.PayPal.Calls);

        // Until PayPal shows the new plan, nothing changes here.
        await ConfirmAsync(change.Session.Id);
        Assert.Equal(monthly.Id, (await SubscriptionAsync(tenant)).PlanPriceId);

        _app.PayPal.Subscriptions[subscriptionId] = _app.PayPal.Subscriptions[subscriptionId] with { PlanId = yearly.PayPalPlanId };
        Assert.Equal(CheckoutStatus.Active, (await ConfirmAsync(change.Session.Id, CheckoutSource.Webhook)).Session!.Status);
        Assert.Equal(yearly.Id, (await SubscriptionAsync(tenant)).PlanPriceId);
        Assert.Equal(new[] { "subscription.activated", "subscription.plan-changed" }, await AuditAsync(tenant, "subscription."));
        Assert.Single(_app.Emails.To(owner), e => e.Subject == "Your plan has changed");

        // Cancel: PayPal first, then here; access until the period end.
        var cancelled = await UseAsync(sp => sp.GetRequiredService<CheckoutService>().CancelSubscriptionAsync(tenant, "Closing the centre", "user:test"));
        Assert.True(cancelled.Succeeded, cancelled.Error);
        Assert.Contains($"CancelSubscription {subscriptionId}", _app.PayPal.Calls);
        var s = await SubscriptionAsync(tenant);
        Assert.Equal((SubscriptionStatus.Canceled, true), (s.Status, s.CancelAtPeriodEnd));
        Assert.True(s.HasFullAccess(DateTime.UtcNow));
        Assert.Single(_app.Emails.To(owner), e => e.Subject == "Your subscription is cancelled");
        Assert.False((await UseAsync(sp => sp.GetRequiredService<CheckoutService>().CancelSubscriptionAsync(tenant, "again", "user:test"))).Succeeded);
    }
}
