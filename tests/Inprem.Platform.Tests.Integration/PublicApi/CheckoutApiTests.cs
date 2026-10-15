using System.Net;
using System.Text;
using System.Text.Json;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.PublicApi;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.PublicApi;

/// <summary>
/// Phase 3 step 3.4 (J2, J5; P3-4, P3-5, P3-6): the website's checkout over the Phase 2 use cases:
/// a "subscribe" signup pays and becomes Active only when PayPal confirms; status polling, confirm,
/// cancel; tokens see only their own checkouts; return addresses on the website only; a PayPal
/// outage can be retried with the same key.
/// </summary>
public class CheckoutApiTests : IClassFixture<PublicApiFixture>
{
    private const string Api = "/api/public/v1";
    private const string Return = PublicApiFixture.WebsiteOrigin + "/subscribe/return";
    private const string Cancel = PublicApiFixture.WebsiteOrigin + "/subscribe/cancel";
    private readonly PublicApiFixture _app;

    public CheckoutApiTests(PublicApiFixture app) => _app = app;

    private async Task<PlanPrice> PublishedPriceAsync(bool publish = true)
    {
        using var scope = _app.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var id = (await plans.CreateAsync($"ca-{Guid.NewGuid():N}"[..12], new("Checkout API plan", null, null, null, 30, true), BillingCycle.Monthly, 22m, "USD", "test")).PlanId!.Value;
        if (publish) Assert.True((await scope.ServiceProvider.GetRequiredService<PlanPublisher>().PublishAsync(id, "test")).Succeeded);
        return (await plans.FindAsync(id))!.Prices[0];
    }

    private static HttpRequestMessage Send(HttpMethod method, string path, string token, object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(method, Api + path);
        request.Headers.Authorization = new("Bearer", token);
        if (body != null || method == HttpMethod.Post) request.Content = new StringContent(body == null ? "" : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (key != null) request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private async Task<(Guid SignupId, string Token, int TenantId)> SignupAsync(PlanPrice price, string mode = "subscribe")
    {
        using var client = _app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, Api + "/signups")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                organizationName = "Checkout Centre", slug = $"ca-{Guid.NewGuid():N}"[..14], timeZone = "UTC",
                owner = new { firstName = "Kofi", lastName = "Mensah", email = $"ca-{Guid.NewGuid():N}"[..14] + "@checkout.test" },
                consent = new { acceptTerms = true, acceptPrivacyPolicy = true }, planPriceId = price.Id, mode, captchaToken = FakeCaptchaVerifier.Pass,
            }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var json = await PublicApiFixture.JsonAsync(await client.SendAsync(request));
        var id = json.GetProperty("signupId").GetGuid();
        await using var db = _app.CreatePlatformDbContext();
        return (id, json.GetProperty("signupToken").GetString()!, (await db.Signups.AsNoTracking().SingleAsync(s => s.Id == id)).TenantId);
    }

    private static object Checkout(Guid priceId, string purpose = "new", string returnUrl = Return) => new { planPriceId = priceId, purpose, returnUrl, cancelUrl = Cancel };

    private static async Task<string> CodeAsync(HttpResponseMessage response) => (await PublicApiFixture.JsonAsync(response)).GetProperty("code").GetString()!;

    private async Task<SubscriptionStatus> SubscriptionAsync(int tenantId)
    {
        await using var db = _app.CreatePlatformDbContext();
        return (await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId)).Status;
    }

    [Fact]
    public async Task A_Subscribe_Signup_Pays_And_Is_Active_Only_When_PayPal_Confirms()
    {
        var price = await PublishedPriceAsync();
        var (_, token, tenant) = await SignupAsync(price);
        using var website = _app.CreateClient();
        var key = Guid.NewGuid().ToString();

        var created = await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id), key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await PublicApiFixture.JsonAsync(created);
        var id = json.GetProperty("checkoutSessionId").GetGuid();
        Assert.StartsWith("https://www.sandbox.paypal.com/", json.GetProperty("approvalUrl").GetString());
        Assert.True(json.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow.AddHours(2));

        // The same request again: the same checkout, one PayPal subscription.
        var again = await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id), key)));
        Assert.Equal(id, again.GetProperty("checkoutSessionId").GetGuid());

        // Back from PayPal: confirm says AwaitingApproval while PayPal has not confirmed; nothing is active.
        var waiting = await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Post, $"/checkout-sessions/{id}/confirm", token)));
        Assert.Equal(("AwaitingApproval", "PendingActivation"), (waiting.GetProperty("status").GetString(), waiting.GetProperty("subscriptionStatus").GetString()));
        Assert.Equal(("Checkout API plan", "Monthly", "22.00"), (waiting.GetProperty("plan").GetProperty("name").GetString(), waiting.GetProperty("plan").GetProperty("billingCycle").GetString(),
            waiting.GetProperty("plan").GetProperty("price").GetProperty("amount").GetString()));

        // PayPal confirms; the website polls.
        await using (var db = _app.CreatePlatformDbContext())
        {
            var session = await db.CheckoutSessions.AsNoTracking().SingleAsync(c => c.Id == id);
            Assert.Equal(CheckoutPurpose.New, session.Purpose);
            _app.PayPal.SetState(session.ExternalSubscriptionId!, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
        }
        var active = await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Post, $"/checkout-sessions/{id}/confirm", token)));
        Assert.Equal(("Active", "Active"), (active.GetProperty("status").GetString(), active.GetProperty("subscriptionStatus").GetString()));
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)).ToString("yyyy-MM-dd"), active.GetProperty("nextBillingDate").GetString());
        Assert.Contains("active", active.GetProperty("message").GetString());
        Assert.Equal("Active", (await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Get, $"/checkout-sessions/{id}", token)))).GetProperty("status").GetString());
        Assert.Equal(SubscriptionStatus.Active, await SubscriptionAsync(tenant));

        // Nothing left to pay now.
        Assert.Equal(Problems.ValidationFailed, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id), Guid.NewGuid().ToString()))));
    }

    [Fact]
    public async Task Tokens_See_Only_Their_Own_Checkouts()
    {
        var price = await PublishedPriceAsync();
        var (_, token, _) = await SignupAsync(price);
        var (_, other, _) = await SignupAsync(price);
        using var website = _app.CreateClient();
        var id = (await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id), Guid.NewGuid().ToString())))).GetProperty("checkoutSessionId").GetGuid();

        Assert.Equal(Problems.NotFound, await CodeAsync(await website.SendAsync(Send(HttpMethod.Get, $"/checkout-sessions/{id}", other))));
        Assert.Equal(Problems.NotFound, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, $"/checkout-sessions/{id}/confirm", other))));
        Assert.Equal(Problems.NotFound, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, $"/checkout-sessions/{id}/cancel", await _app.TokenAsync(billing: true)))));
        Assert.Equal(Problems.NotFound, await CodeAsync(await website.SendAsync(Send(HttpMethod.Get, $"/checkout-sessions/{Guid.NewGuid()}", token))));
    }

    [Fact]
    public async Task Refusals_Have_Their_Codes()
    {
        var price = await PublishedPriceAsync();
        var (_, token, _) = await SignupAsync(price);
        using var website = _app.CreateClient();
        website.DefaultRequestHeaders.Add("X-Api-Key", PublicApiFixture.ApiKey);
        string Key() => Guid.NewGuid().ToString();

        Assert.Equal(Problems.ReturnUrlNotAllowed, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id, returnUrl: "https://evil.test/return"), Key()))));
        Assert.Equal(Problems.ReturnUrlNotAllowed, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id, returnUrl: "http://www.website.test/return"), Key()))));
        Assert.Equal(Problems.PlanNotAvailable, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout((await PublishedPriceAsync(publish: false)).Id), Key()))));
        Assert.Equal(Problems.ValidationFailed, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id, purpose: "convert"), Key()))));
        Assert.Equal(Problems.ValidationFailed, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, new { purpose = "free" }, Key()))));
        Assert.Equal(Problems.TokenMissing, await CodeAsync(await website.SendAsync(new HttpRequestMessage(HttpMethod.Post, Api + "/checkout-sessions") { Content = new StringContent("{}", Encoding.UTF8, "application/json") })));

        // A billing token cannot check out as new.
        var billing = await _app.TokenAsync(billing: true, tenantId: _app.Data.Alpha.Id);
        Assert.Equal(Problems.ValidationFailed, await CodeAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", billing, Checkout(price.Id), Key()))));
    }

    [Fact]
    public async Task Cancel_At_PayPal_Then_Try_Again_And_A_PayPal_Outage_Can_Be_Retried()
    {
        var price = await PublishedPriceAsync();
        var (_, token, tenant) = await SignupAsync(price);
        using var website = _app.CreateClient();

        var first = (await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id), Guid.NewGuid().ToString())))).GetProperty("checkoutSessionId").GetGuid();
        var cancelled = await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Post, $"/checkout-sessions/{first}/cancel", token)));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("Cancelled", (await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Post, $"/checkout-sessions/{first}/cancel", token)))).GetProperty("status").GetString()); // already final
        Assert.Equal(SubscriptionStatus.PendingActivation, await SubscriptionAsync(tenant)); // the organization stays as it was (J5)

        // PayPal is down: 502, safe to retry with the same key.
        var key = Guid.NewGuid().ToString();
        _app.PayPal.FailNextCalls = 1;
        HttpResponseMessage down;
        try
        {
            down = await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id), key));
        }
        finally
        {
            _app.PayPal.FailNextCalls = 0;
        }
        Assert.Equal((HttpStatusCode.BadGateway, Problems.PaymentProviderUnavailable), (down.StatusCode, await CodeAsync(down)));
        Assert.Equal(HttpStatusCode.Created, (await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", token, Checkout(price.Id), key))).StatusCode);
    }

    [Fact]
    public async Task A_Billing_Token_Converts_A_Trial()
    {
        var price = await PublishedPriceAsync();
        var (_, _, tenant) = await SignupAsync(price, mode: "trial");
        var billing = await _app.TokenAsync(billing: true, tenantId: tenant);
        using var website = _app.CreateClient();

        var created = await website.SendAsync(Send(HttpMethod.Post, "/checkout-sessions", billing, Checkout(price.Id, purpose: "convert"), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await PublicApiFixture.JsonAsync(created)).GetProperty("checkoutSessionId").GetGuid();
        var status = await PublicApiFixture.JsonAsync(await website.SendAsync(Send(HttpMethod.Get, $"/checkout-sessions/{id}", billing)));
        Assert.Equal(("AwaitingApproval", "Trialing"), (status.GetProperty("status").GetString(), status.GetProperty("subscriptionStatus").GetString()));
    }
}
