using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 2 step 2.6 (P2-6): reconciliation corrects a dropped webhook within one cycle; stale
/// checkouts expire or fail; ended trials expire; PayPal failures for one subscription do not stop
/// the others; the provider gets a summary of corrections.
/// </summary>
public class ReconciliationTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public ReconciliationTests(ProviderFixture app) => _app = app;

    private async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<ReconciliationReport> CheckoutsAsync() => UseAsync(sp => sp.GetRequiredService<BillingReconciliation>().CheckoutsAsync());

    private Task<ReconciliationReport> DailyAsync() => UseAsync(sp => sp.GetRequiredService<BillingReconciliation>().DailyAsync());

    private async Task<(int TenantId, CheckoutSession Session)> CheckoutAsync(int trialDays = 7) => await UseAsync(async sp =>
    {
        var plans = sp.GetRequiredService<PlanService>();
        var planId = (await plans.CreateAsync($"rc-{Guid.NewGuid():N}"[..12], new("Reconcile plan", null, null, null, 7, true), BillingCycle.Monthly, 15m, "USD", "test")).PlanId!.Value;
        await sp.GetRequiredService<PlanPublisher>().PublishAsync(planId, "test");
        var price = (await plans.FindAsync(planId))!.Prices[0];
        var tenant = (await sp.GetRequiredService<OrganizationService>().CreateAsync(new NewOrganization($"Reconcile Org {Guid.NewGuid():N}"[..23], $"rc-{Guid.NewGuid():N}"[..14],
            "UTC", null, null, "R", "Owner", $"r-{Guid.NewGuid():N}"[..10] + "@reconcile.test", null, planId, price.Id, BillingMode.PayPal, trialDays), "test", "https://localhost")).TenantId!.Value;
        var started = await sp.GetRequiredService<CheckoutService>().StartAsync(new StartCheckout(CheckoutPurpose.Convert, tenant, price.Id, "https://localhost/Billing/Return", "https://localhost/Billing", Guid.NewGuid().ToString(), "test"));
        return (tenant, started.Session!);
    });

    private async Task<Subscription> SubscriptionAsync(int tenantId)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
    }

    private async Task<CheckoutStatus> StatusAsync(Guid checkout)
    {
        await using var db = _app.CreatePlatformDbContext();
        return (await db.CheckoutSessions.AsNoTracking().SingleAsync(c => c.Id == checkout)).Status;
    }

    private async Task<List<string>> AuditAsync(int tenantId, string prefix)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.Where(a => a.TenantId == tenantId && a.Action.StartsWith(prefix)).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
    }

    private async Task<string> BillingProviderAsync()
    {
        var email = $"bill-{Guid.NewGuid():N}"[..14] + "@reconcile.test";
        await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Billing);
        return email;
    }

    private async Task WithClockAsync(TimeSpan ahead, Func<Task> work)
    {
        _app.Clock.Advance(ahead);
        try
        {
            await work();
        }
        finally
        {
            _app.Clock.Advance(-ahead);
        }
    }

    [Fact]
    public async Task A_Dropped_Activation_Webhook_Is_Corrected_By_The_Next_5_Minute_Run()
    {
        var (tenant, session) = await CheckoutAsync();
        _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1)); // no webhook arrives

        var report = await CheckoutsAsync();

        Assert.Contains(report.Corrections, c => c.Contains(session.Reference) && c.EndsWith("Active"));
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(tenant)).Status);
        Assert.Equal(new[] { "subscription.activated" }, await AuditAsync(tenant, "subscription."));
        await CheckoutsAsync(); // the next run: no longer open, nothing more
        Assert.Single(await AuditAsync(tenant, "subscription."));
    }

    [Fact]
    public async Task A_Dropped_Suspension_Webhook_Is_Corrected_By_The_Daily_Run_And_The_Provider_Is_Told()
    {
        var billing = await BillingProviderAsync();
        var (tenant, session) = await CheckoutAsync();
        var id = session.ExternalSubscriptionId!;
        _app.PayPal.SetState(id, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        await CheckoutsAsync();

        // PayPal suspends after failed payments; the webhooks never arrive.
        _app.PayPal.Subscriptions[id] = _app.PayPal.Subscriptions[id] with { State = PayPalSubscriptionState.Suspended, OutstandingBalance = 15m, FailedPaymentsCount = 2 };
        var report = await DailyAsync();

        Assert.Contains(report.Corrections, c => c.Contains(id) && c.Contains("subscription.suspended"));
        Assert.Equal(SubscriptionStatus.Suspended, (await SubscriptionAsync(tenant)).Status);
        await using (var db = _app.CreatePlatformDbContext())
        {
            Assert.True(await db.AuditEntries.AnyAsync(a => a.TenantId == tenant && a.Action == "subscription.suspended" && a.Actor == "job:reconciliation"));
        }
        var summary = Assert.Single(_app.Emails.To(billing), e => e.Subject.StartsWith("[Inprem billing] Daily PayPal check"));
        Assert.Contains(id, summary.HtmlBody);

        // Next day, nothing to correct for it.
        var again = await DailyAsync();
        Assert.DoesNotContain(again.Corrections, c => c.Contains(id));
    }

    [Fact]
    public async Task Stale_Checkouts_Expire_Or_Fail()
    {
        var billing = await BillingProviderAsync();

        // Never approved: expires after 3 hours.
        var (_, unapproved) = await CheckoutAsync();
        // Approved, but PayPal never makes it ACTIVE: fails after 24 hours and alerts.
        var (stuckTenant, stuck) = await CheckoutAsync();
        _app.PayPal.SetState(stuck.ExternalSubscriptionId!, PayPalSubscriptionState.Approved);
        await CheckoutsAsync();
        Assert.Equal(CheckoutStatus.Approved, await StatusAsync(stuck.Id));
        Assert.Equal(SubscriptionStatus.PendingActivation, (await SubscriptionAsync(stuckTenant)).Status);

        await WithClockAsync(CheckoutSession.Lifetime + TimeSpan.FromMinutes(1), async () =>
        {
            await CheckoutsAsync();
            Assert.Equal(CheckoutStatus.Expired, await StatusAsync(unapproved.Id));
            Assert.Equal(CheckoutStatus.Approved, await StatusAsync(stuck.Id)); // approved: waits for PayPal
        });
        await WithClockAsync(CheckoutService.ApprovedLimit + TimeSpan.FromMinutes(1), async () =>
        {
            await CheckoutsAsync();
            Assert.Equal(CheckoutStatus.Failed, await StatusAsync(stuck.Id));
        });
        Assert.Contains(_app.Emails.To(billing), e => e.Subject == "[Inprem billing] Checkout stuck at Approved" && e.HtmlBody.Contains(stuck.Reference));
    }

    [Fact]
    public async Task Ended_Trials_Expire_In_The_Daily_Run_And_Lose_Access_At_Once()
    {
        var (tenant, _) = await CheckoutAsync(trialDays: 1);
        Assert.True((await SubscriptionAsync(tenant)).HasFullAccess(_app.Clock.UtcNow));

        await WithClockAsync(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1), async () =>
        {
            Assert.False((await SubscriptionAsync(tenant)).HasFullAccess(_app.Clock.UtcNow)); // before the job runs
            var report = await DailyAsync();
            Assert.Contains($"Organization {tenant}: trial ended", report.Corrections);
        });

        Assert.Equal(SubscriptionStatus.Expired, (await SubscriptionAsync(tenant)).Status);
        Assert.Equal(new[] { "subscription.trial-expired" }, await AuditAsync(tenant, "subscription."));
    }

    [Fact]
    public async Task PayPal_Failing_For_One_Does_Not_Stop_The_Others()
    {
        var (first, a) = await CheckoutAsync();
        var (second, b) = await CheckoutAsync();
        _app.PayPal.SetState(a.ExternalSubscriptionId!, PayPalSubscriptionState.Active);
        _app.PayPal.SetState(b.ExternalSubscriptionId!, PayPalSubscriptionState.Active);

        // The oldest open checkouts go first; let some calls fail along the way.
        _app.PayPal.FailNextCalls = 1;
        try
        {
            await CheckoutsAsync();
        }
        finally
        {
            _app.PayPal.FailNextCalls = 0;
        }
        await CheckoutsAsync(); // the next run catches up whatever was skipped

        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(first)).Status);
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(second)).Status);
    }
}
