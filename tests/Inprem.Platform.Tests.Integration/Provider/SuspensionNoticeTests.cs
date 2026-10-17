using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Suspending a paying customer (developer's decision, 2026-10-17): the owners are emailed on
/// suspension and reactivation, the reason only when the provider chooses to share it, and the
/// console warns before suspending an organization that pays through PayPal.
/// </summary>
public class SuspensionNoticeTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public SuspensionNoticeTests(ProviderFixture app) => _app = app;

    private int Tenant => _app.Data.Gamma.Id;

    private async Task<HttpClient> OwnerConsoleAsync()
    {
        var email = $"sus-{Guid.NewGuid():N}"[..14] + "@console.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Owner);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return client;
    }

    [Fact]
    public async Task Owners_Are_Emailed_And_The_Reason_Is_Shared_Only_When_Chosen()
    {
        var owner = _app.Data.Alpha.Owner!.Email;
        using var console = await OwnerConsoleAsync();
        var alpha = _app.Data.Alpha.Id;

        // Not shared (the default).
        await PostAsync(console, $"/Provider/Tenants/Suspend/{alpha}", ("Reason", "Fraud investigation 4471"));
        var suspended = _app.Emails.To(owner).Last(e => e.Subject.Contains("has been suspended"));
        Assert.Contains("Please contact Inprem", suspended.HtmlBody);
        Assert.Contains("run reports and download exports", suspended.HtmlBody);
        Assert.DoesNotContain("Fraud investigation 4471", suspended.HtmlBody);

        await PostAsync(console, $"/Provider/Tenants/Reactivate/{alpha}", ("Reason", "Cleared"));
        Assert.Contains(_app.Emails.To(owner), e => e.Subject.Contains("is active again"));

        // Shared when ticked.
        await PostAsync(console, $"/Provider/Tenants/Suspend/{alpha}", ("Reason", "Unpaid invoice INV-100123"), ("ShareReason", "true"));
        Assert.Contains("Reason: Unpaid invoice INV-100123", _app.Emails.To(owner).Last(e => e.Subject.Contains("has been suspended")).HtmlBody);
        await PostAsync(console, $"/Provider/Tenants/Reactivate/{alpha}", ("Reason", "Paid"));

        await using var db = _app.CreatePlatformDbContext();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.TenantId == alpha && a.Action == "organization.suspended" && a.Details!.Contains("(shared with the owners)")));
    }

    [Fact]
    public async Task The_Console_Warns_Before_Suspending_An_Organization_That_Pays_By_PayPal()
    {
        using var console = await OwnerConsoleAsync();
        Assert.Null((await GetDocumentAsync(console, $"/Provider/Tenants/Suspend/{Tenant}")).QuerySelector("[data-testid=paypal-warning]"));

        using (var scope = _app.Services.CreateScope())
        {
            var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
            var planId = (await plans.CreateAsync($"sn-{Guid.NewGuid():N}"[..12], new("Suspension plan", null, null, null, 0, true), BillingCycle.Monthly, 9m, "USD", "test")).PlanId!.Value;
            var plan = (await plans.FindAsync(planId))!;
            await using var db = _app.CreatePlatformDbContext();
            await db.Subscriptions.Where(s => s.TenantId == Tenant).ExecuteDeleteAsync();
            var now = _app.Clock.UtcNow;
            var subscription = Subscription.Start(Tenant, plan, plan.Prices[0], BillingMode.PayPal, 30, now.AddDays(-5));
            subscription.Activate($"I-{Guid.NewGuid():N}"[..14], plan.Prices[0], now.AddDays(-5), now.AddDays(25), now.AddDays(-5));
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
        }

        var warning = (await GetDocumentAsync(console, $"/Provider/Tenants/Suspend/{Tenant}")).QuerySelector("[data-testid=paypal-warning]");
        Assert.NotNull(warning);
        Assert.Contains("PayPal keeps charging it while it is suspended", warning!.TextContent);
    }
}
