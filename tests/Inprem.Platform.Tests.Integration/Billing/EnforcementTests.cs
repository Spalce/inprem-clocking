using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 2 step 2.7 (P2-5, C3-2 to C3-4): an organization without full access is read-only
/// (no saving, kiosk paused) while every page, report and export still works; payment problems and
/// the last days of a trial show a banner; active organizations see nothing new.
/// </summary>
public class EnforcementTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public EnforcementTests(ProviderFixture app) => _app = app;

    private int Tenant => _app.Data.Beta.Id;

    private long StaffId => _app.Data.Beta.Staff[0].Id;

    /// <summary>Gives Beta a subscription in the state <paramref name="shape"/> makes from a 30-day PayPal trial.</summary>
    private async Task SubscriptionAsync(Action<Subscription, PlanPrice, DateTime> shape)
    {
        using var scope = _app.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var planId = (await plans.CreateAsync($"en-{Guid.NewGuid():N}"[..12], new("Enforcement plan", null, null, null, 30, true), BillingCycle.Monthly, 9m, "USD", "test")).PlanId!.Value;
        var plan = (await plans.FindAsync(planId))!;

        await using var db = _app.CreatePlatformDbContext();
        await db.Subscriptions.Where(s => s.TenantId == Tenant).ExecuteDeleteAsync();
        var now = _app.Clock.UtcNow;
        var subscription = Subscription.Start(Tenant, plan, plan.Prices[0], BillingMode.PayPal, 30, now.AddDays(-20));
        shape(subscription, plan.Prices[0], now);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    private static void Suspended(Subscription s, PlanPrice price, DateTime now)
    {
        s.Activate($"I-{Guid.NewGuid():N}"[..14], price, now.AddDays(-20), now.AddDays(10), now.AddDays(-20));
        s.MarkPastDue(9m, now.AddDays(-5));
        s.Suspend(now.AddDays(-1));
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _app.CreateClient();
        await SignIn.AsAsync(client, _app.Data.Beta.Admin!.Email, _app.Data.Password);
        return client;
    }

    private async Task<string?> BannerAsync(HttpClient client)
    {
        var page = await client.GetAsync("/BackOffice");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = new HtmlParser().ParseDocument(await page.Content.ReadAsStringAsync());
        return html.QuerySelector("[data-testid=billing-banner]")?.GetAttribute("data-kind");
    }

    private async Task<int> ClockingsTodayAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InpremClockingApp.Data.ApplicationDbContext>();
        return await db.ClockingsStaff.IgnoreQueryFilters().CountAsync(c => c.StafId == StaffId && c.CreatedAt >= DateTime.Today);
    }

    [Fact]
    public async Task A_Suspended_Organization_Is_Read_Only_But_Every_Page_Report_And_Export_Works()
    {
        await SubscriptionAsync(Suspended);
        using var admin = await AdminAsync();

        // Pages and the banner.
        Assert.Equal("read-only", await BannerAsync(admin));
        foreach (var page in new[] { "/Staff", "/Volunteer", "/StaffClocking", "/StaffReport" })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(page)).StatusCode);

        // Reports and exports (POSTs that only read) still work.
        var body = $$"""{"Id":{{StaffId}},"StartDate":"2026-03-01","EndDate":"2026-03-31"}""";
        var report = await admin.PostAsync("/api/reports/staff-clocking-one", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Contains("\"contents\"", await report.Content.ReadAsStringAsync());
        var export = await admin.PostAsync("/api/reports/export/staff-clocking?format=csv", new StringContent("""{"StartDate":"2026-03-01","EndDate":"2026-03-31"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);

        // Saving from a page: sent to the explanation page, nothing saved.
        var save = await admin.PostAsync("/Staff?handler=Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["FirstName"] = "Blocked" }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal("/Account/ReadOnly?kiosk=false", save.Headers.Location!.ToString());
        var explained = new HtmlParser().ParseDocument(await (await admin.GetAsync(save.Headers.Location)).Content.ReadAsStringAsync());
        Assert.Contains("suspended", explained.QuerySelector("[data-testid=reason]")!.TextContent);

        // Saving through the API: a clear 403 message.
        var api = await admin.PostAsync("/api/staff/save", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
        Assert.Contains("read-only", await api.Content.ReadAsStringAsync());

        // The kiosk pauses: its pages and its actions (which are GETs).
        var kiosk = await admin.GetAsync("/StaffAttendance");
        Assert.Equal("/Account/ReadOnly?kiosk=true", kiosk.Headers.Location!.ToString());
        Assert.Contains("kiosk is paused", (await (await admin.GetAsync(kiosk.Headers.Location)).Content.ReadAsStringAsync()));
        var before = await ClockingsTodayAsync();
        var clockIn = await admin.GetAsync($"/api/controls/staff-clockin/{StaffId}");
        Assert.Equal(HttpStatusCode.Forbidden, clockIn.StatusCode);
        Assert.Contains("kiosk is paused", await clockIn.Content.ReadAsStringAsync());
        Assert.Equal(before, await ClockingsTodayAsync());

        // Signing out still works.
        var signOut = await admin.PostAsync("/Identity/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.DoesNotContain("/Account/ReadOnly", signOut.Headers.Location?.ToString() ?? "");
    }

    [Theory]
    [InlineData("trial-over")]
    [InlineData("expired")]
    [InlineData("cancelled-ended")]
    public async Task Expired_And_Ended_Organizations_Are_Read_Only(string state)
    {
        await SubscriptionAsync((s, price, now) =>
        {
            switch (state)
            {
                case "trial-over": // the trial ends in a minute; the clock then moves on, before the daily job marks it
                    s.ExtendTrial(now.AddMinutes(1), now.AddMinutes(-1));
                    break;
                case "expired":
                    s.ExtendTrial(now.AddMinutes(1), now.AddMinutes(-1));
                    s.ExpireTrialIfOver(now.AddMinutes(2));
                    break;
                case "cancelled-ended":
                    s.Activate($"I-{Guid.NewGuid():N}"[..14], price, now.AddDays(-40), now.AddDays(-10), now.AddDays(-40));
                    s.Cancel(now.AddDays(-20));
                    break;
            }
        });
        _app.Clock.Advance(TimeSpan.FromMinutes(5));
        try
        {
            using var admin = await AdminAsync();
            Assert.Equal("read-only", await BannerAsync(admin));
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/controls/staff-clockin/{StaffId}")).StatusCode);
        }
        finally
        {
            _app.Clock.Advance(-TimeSpan.FromMinutes(5));
        }
    }

    [Fact]
    public async Task A_Payment_Problem_And_The_Last_Trial_Days_Show_A_Banner_But_Nothing_Is_Blocked()
    {
        await SubscriptionAsync((s, price, now) =>
        {
            s.Activate($"I-{Guid.NewGuid():N}"[..14], price, now.AddDays(-20), now.AddDays(10), now.AddDays(-20));
            s.MarkPastDue(9m, now.AddDays(-1));
        });
        using (var admin = await AdminAsync())
        {
            Assert.Equal("payment", await BannerAsync(admin));
            Assert.NotEqual(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/controls/staff-clockin/{StaffId}")).StatusCode);
        }

        await SubscriptionAsync((_, _, _) => { }); // a 30-day trial started 20 days ago: 10 days left
        using (var admin = await AdminAsync())
        {
            Assert.Equal("trial", await BannerAsync(admin));
            Assert.NotEqual("/Account/ReadOnly?kiosk=true", (await admin.GetAsync("/StaffAttendance")).Headers.Location?.ToString());
        }
    }

    [Fact]
    public async Task Active_Free_And_Long_Trial_Organizations_See_No_Change()
    {
        await SubscriptionAsync((s, price, now) => s.Activate($"I-{Guid.NewGuid():N}"[..14], price, now, now.AddMonths(1), now));
        using (var admin = await AdminAsync())
        {
            Assert.Null(await BannerAsync(admin));
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/StaffAttendance")).StatusCode);
        }

        await using (var db = _app.CreatePlatformDbContext())
        {
            await db.Subscriptions.Where(s => s.TenantId == Tenant).ExecuteDeleteAsync();
            db.Subscriptions.Add(Subscription.Start(Tenant, null, null, BillingMode.Free, 0, _app.Clock.UtcNow));
            await db.SaveChangesAsync();
        }
        using (var admin = await AdminAsync())
        {
            Assert.Null(await BannerAsync(admin));
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/StaffAttendance")).StatusCode);
        }
    }

    [Fact]
    public async Task Entitlements_Follow_The_Plan_Limit()
    {
        await SubscriptionAsync((s, _, _) => s.OverrideMaxPeople(2));
        using var scope = _app.Services.CreateScope();
        var access = await scope.ServiceProvider.GetRequiredService<IEntitlementService>().GetAsync(Tenant);
        Assert.True(access.CanAddPeople(1));
        Assert.False(access.CanAddPeople(2));
        Assert.False(access.CanSendSms); // Phase 5
    }
}
