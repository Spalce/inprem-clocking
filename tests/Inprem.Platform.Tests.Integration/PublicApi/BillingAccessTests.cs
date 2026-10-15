using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.PublicApi;
using InpremClockingApp.PublicApi;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.PublicApi;

/// <summary>
/// Phase 3 step 3.5 (J3, J4; P3-3, P3-5): existing owners on the website: the same answer and
/// timing for known and unknown emails; a one-time 15-minute link for owners only; the code works
/// once; the billing token sees its own organization's subscription and can cancel it.
/// </summary>
public class BillingAccessTests : IClassFixture<PublicApiFixture>
{
    private const string Api = "/api/public/v1";
    private readonly PublicApiFixture _app;

    public BillingAccessTests(PublicApiFixture app) => _app = app;

    private static HttpRequestMessage Post(string path, object body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Api + path) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        if (token != null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Get(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static object Ask(string email, string captcha = FakeCaptchaVerifier.Pass) => new { email, captchaToken = captcha };

    private static async Task<string> CodeOf(HttpResponseMessage response) => (await PublicApiFixture.JsonAsync(response)).GetProperty("code").GetString()!;

    /// <summary>The newest billing link emailed to the address: its one-time code.</summary>
    private string LinkCode(string email)
    {
        var link = LinkSentTo(_app.Emails, email, "/billing?code=");
        return Uri.UnescapeDataString(link[(link.IndexOf("code=", StringComparison.Ordinal) + 5)..]);
    }

    private async Task<string> BillingTokenAsync(string ownerEmail)
    {
        using var website = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await website.SendAsync(Post("/billing-access/requests", Ask(ownerEmail)))).StatusCode);
        await _app.RunBillingAccessJobsAsync();
        var session = await website.SendAsync(Post("/billing-access/sessions", new { code = LinkCode(ownerEmail) }));
        Assert.Equal(HttpStatusCode.Created, session.StatusCode);
        return (await PublicApiFixture.JsonAsync(session)).GetProperty("billingAccessToken").GetString()!;
    }

    [Fact]
    public async Task Known_And_Unknown_Emails_Get_The_Same_Answer_In_The_Same_Time_And_Only_Owners_Get_A_Link()
    {
        var owner = _app.Data.Beta.Owner!.Email;
        var admin = _app.Data.Beta.Admin!.Email;
        var unknown = $"nobody-{Guid.NewGuid():N}"[..14] + "@unknown.test";
        await using (var db = _app.CreatePlatformDbContext()) await db.BillingAccessCodes.ExecuteDeleteAsync();
        using var website = _app.CreateClient();
        website.DefaultRequestHeaders.Add("X-Test-Address", "10.77.0.1");

        async Task<(string Body, HttpStatusCode Status, TimeSpan Time)> AskAsync(string email)
        {
            var watch = Stopwatch.StartNew();
            var response = await website.SendAsync(Post("/billing-access/requests", Ask(email)));
            watch.Stop();
            return (await response.Content.ReadAsStringAsync(), response.StatusCode, watch.Elapsed);
        }

        var linksBefore = _app.Emails.To(owner).Count(e => e.Subject == "Manage your Inprem Clock-in subscription");
        await AskAsync(unknown); // warm up
        var known = await AskAsync(owner);
        var other = await AskAsync(unknown);
        var notOwner = await AskAsync(admin);
        Assert.Equal((HttpStatusCode.Accepted, known.Body), (other.Status, other.Body));
        Assert.Equal((HttpStatusCode.Accepted, known.Body), (notOwner.Status, notOwner.Body));
        Assert.Contains(PublicApiEndpoints.BillingAccessAcceptedMessage, known.Body);
        Assert.All(new[] { known.Time, other.Time, notOwner.Time }, t => Assert.True(t >= PublicApiEndpoints.BillingAccessMinimumTime - TimeSpan.FromMilliseconds(20), $"{t.TotalMilliseconds} ms"));
        Assert.True((known.Time - other.Time).Duration() < TimeSpan.FromMilliseconds(250), $"known {known.Time.TotalMilliseconds} ms, unknown {other.Time.TotalMilliseconds} ms");

        // Only now the jobs run: an email for the owner, nothing for the admin or the unknown address.
        await _app.RunBillingAccessJobsAsync();
        var links = _app.Emails.To(owner).Where(e => e.Subject == "Manage your Inprem Clock-in subscription").ToList();
        Assert.Equal(linksBefore + 1, links.Count);
        var mail = links[^1];
        Assert.Contains(PublicApiFixture.WebsiteOrigin + "/billing?code=", mail.HtmlBody);
        Assert.Contains("Beta", mail.HtmlBody);
        Assert.DoesNotContain(_app.Emails.To(admin), e => e.Subject == "Manage your Inprem Clock-in subscription");
        Assert.Empty(_app.Emails.To(unknown));
        await using (var db = _app.CreatePlatformDbContext())
        {
            Assert.Equal(1, await db.BillingAccessCodes.CountAsync()); // nothing stored for the others
            Assert.DoesNotContain(LinkCode(owner), (await db.BillingAccessCodes.SingleAsync()).CodeHash); // only the hash is kept
        }

        // Bad requests answer the same for everyone.
        Assert.Equal(Problems.ValidationFailed, await CodeOf(await website.SendAsync(Post("/billing-access/requests", Ask("not-an-email")))));
        Assert.Equal(Problems.CaptchaFailed, await CodeOf(await website.SendAsync(Post("/billing-access/requests", Ask(owner, "robot")))));
    }

    [Fact]
    public async Task The_Code_Works_Once_For_15_Minutes_And_At_Most_3_Links_An_Hour()
    {
        var owner = _app.Data.Alpha.Owner!.Email;
        using var website = _app.CreateClient();
        website.DefaultRequestHeaders.Add("X-Api-Key", PublicApiFixture.ApiKey);

        await website.SendAsync(Post("/billing-access/requests", Ask(owner)));
        await _app.RunBillingAccessJobsAsync();
        var code = LinkCode(owner);
        var first = await website.SendAsync(Post("/billing-access/sessions", new { code }));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var json = await PublicApiFixture.JsonAsync(first);
        Assert.Equal("Alpha", json.GetProperty("organizationName").GetString());
        Assert.True(json.GetProperty("expiresAt").GetDateTime() <= DateTime.UtcNow.Add(ApiToken.BillingAccessLifetime).AddMinutes(1));
        Assert.Equal(Problems.InvalidCode, await CodeOf(await website.SendAsync(Post("/billing-access/sessions", new { code })))); // once
        Assert.Equal(Problems.InvalidCode, await CodeOf(await website.SendAsync(Post("/billing-access/sessions", new { code = "made-up-code" }))));

        // Expired after 15 minutes.
        await website.SendAsync(Post("/billing-access/requests", Ask(owner)));
        await _app.RunBillingAccessJobsAsync();
        var late = LinkCode(owner);
        _app.Clock.Advance(BillingAccessCode.Lifetime + TimeSpan.FromSeconds(1));
        try
        {
            Assert.Equal(Problems.InvalidCode, await CodeOf(await website.SendAsync(Post("/billing-access/sessions", new { code = late }))));
        }
        finally
        {
            _app.Clock.Advance(-(BillingAccessCode.Lifetime + TimeSpan.FromSeconds(1)));
        }

        // At most 3 links per organization per hour: the fourth request sends nothing.
        var before = _app.Emails.To(owner).Count();
        await website.SendAsync(Post("/billing-access/requests", Ask(owner)));
        await website.SendAsync(Post("/billing-access/requests", Ask(owner)));
        await _app.RunBillingAccessJobsAsync();
        Assert.Equal(before + 1, _app.Emails.To(owner).Count());
    }

    [Fact]
    public async Task The_Billing_Token_Manages_Its_Own_Organization_Only()
    {
        // Beta (its owner is known) with an active PayPal subscription.
        var tenant = _app.Data.Beta.Id;
        using (var scope = _app.Services.CreateScope())
        {
            var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
            var planId = (await plans.CreateAsync($"ba-{Guid.NewGuid():N}"[..12], new("Account plan", null, null, null, 30, true), BillingCycle.Monthly, 18m, "USD", "test")).PlanId!.Value;
            var price = (await plans.FindAsync(planId))!.Prices[0];
            await using var db = _app.CreatePlatformDbContext();
            await db.Subscriptions.Where(s => s.TenantId == tenant).ExecuteDeleteAsync();
            var subscription = Subscription.Start(tenant, (await db.Plans.Include(p => p.Prices).SingleAsync(p => p.Id == planId)), price, BillingMode.PayPal, 30, DateTime.UtcNow.AddDays(-40));
            var externalId = $"I-{Guid.NewGuid():N}"[..14];
            subscription.Activate(externalId, price, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(20), DateTime.UtcNow.AddDays(-10));
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
            _app.PayPal.Subscriptions[externalId] = new(externalId, PayPalSubscriptionState.Active, "ACTIVE", null, null, null, DateTime.UtcNow.AddDays(20), null, null, null, 0m, 0);
        }

        var token = await BillingTokenAsync(_app.Data.Beta.Owner!.Email);
        using var website = _app.CreateClient();

        var account = await PublicApiFixture.JsonAsync(await website.SendAsync(Get("/account/subscription", token)));
        Assert.Equal(("Active", "PayPal", false, "18.00"), (account.GetProperty("status").GetString(), account.GetProperty("billingMode").GetString(),
            account.GetProperty("cancelAtPeriodEnd").GetBoolean(), account.GetProperty("plan").GetProperty("price").GetProperty("amount").GetString()));
        Assert.Equal(0, (await PublicApiFixture.JsonAsync(await website.SendAsync(Get("/account/invoices", token)))).GetArrayLength());

        var cancelled = await website.SendAsync(Post("/account/subscription/cancel", new { }, token));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var c = await PublicApiFixture.JsonAsync(cancelled);
        Assert.Equal(("Canceled", true), (c.GetProperty("status").GetString(), c.GetProperty("cancelAtPeriodEnd").GetBoolean()));
        Assert.True(c.GetProperty("accessEndsAt").GetDateTime() > DateTime.UtcNow.AddDays(19));
        Assert.Equal(Problems.SubscriptionNotCancellable, await CodeOf(await website.SendAsync(Post("/account/subscription/cancel", new { }, token))));
        Assert.Equal(Problems.CannotKeep, await CodeOf(await website.SendAsync(Post("/account/subscription/keep", new { }, token))));

        // A signup token is not a billing token.
        Assert.Equal(Problems.TokenInvalid, await CodeOf(await website.SendAsync(Get("/account/subscription", await _app.TokenAsync()))));
    }
}
