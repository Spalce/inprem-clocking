using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Infrastructure.Twilio;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Security;

/// <summary>
/// Step 7.6 (guardrails section 4): the logs never hold passwords, tokens, one-time codes, full
/// emails, full phone numbers or webhook payloads. The real app runs the flows that handle them:
/// website sign-up, email verification with a password, sign-in (wrong and right password), a
/// billing link and its token, a PayPal webhook and an incoming text. Every log line, at the levels
/// a server keeps and at Debug for the app's own code, is then searched for each value.
/// </summary>
public class LogScrubbingTests : IAsyncLifetime
{
    private const string Api = "/api/public/v1";
    private const string TwilioToken = "test-twilio-auth-token";
    private const string WebhookBase = "https://localhost";

    private readonly CapturingLoggerProvider _logs = new();
    private TestDatabase _db = null!;
    private InpremAppFactory _app = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(nameof(LogScrubbingTests));
        await Jobs.HangfireSchema.InstallAsync(_db.ConnectionString); // webhooks queue their work as jobs
        _app = new InpremAppFactory(_db.ConnectionString, new Dictionary<string, string?>
        {
            ["Site:SiteMode"] = "All",
            ["Links:WebsiteBaseUrl"] = "https://www.website.test",
            ["Twilio:AuthToken"] = TwilioToken,
            ["Twilio:WebhookBaseUrl"] = WebhookBase,
        }, configureServices: services =>
        {
            services.AddSingleton<ILoggerProvider>(_logs);
            services.AddLogging(logging => logging
                .AddFilter<CapturingLoggerProvider>("Inprem", LogLevel.Debug)
                .AddFilter<CapturingLoggerProvider>("InpremClockingApp", LogLevel.Debug));
        });
        _ = _app.Server;
        await TestDataSeeder.SeedAsync(_app.Services);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, object? body, string? token = null)
    {
        var request = new HttpRequestMessage(method, Api + path);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        if (token != null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    [Fact]
    public async Task Passwords_Tokens_Codes_Emails_Phones_And_Payloads_Never_Reach_The_Logs()
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        var email = $"scrub-{unique}@signup.test";
        var mobile = "+1555" + Random.Shared.Next(1000000, 9999999);
        var password = $"Scrub-Pass-{unique}!";
        var wrongPassword = $"Wrong-Pass-{unique}!";
        var secrets = new List<(string What, string Value)> { ("owner email", email), ("owner mobile", mobile), ("password", password), ("wrong password", wrongPassword) };

        // A public plan to sign up for.
        Guid priceId;
        using (var scope = _app.Services.CreateScope())
        {
            var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
            var id = (await plans.CreateAsync($"scrub-{unique}", new("Scrub plan", null, null, null, 30, true), BillingCycle.Monthly, 15m, "USD", "test")).PlanId!.Value;
            priceId = (await plans.FindAsync(id))!.Prices[0].Id;
        }

        // 1. Website sign-up.
        using var website = _app.CreateClient();
        var created = await website.SendAsync(Json(HttpMethod.Post, "/signups", new
        {
            organizationName = "Scrub Centre", slug = $"scrub-{unique}", timeZone = "America/New_York",
            owner = new { firstName = "Ama", lastName = "Owusu", email, mobile },
            consent = new { acceptTerms = true, acceptPrivacyPolicy = true, smsOptIn = true },
            planPriceId = priceId, mode = "trial", captchaToken = FakeCaptchaVerifier.Pass,
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var signup = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        var signupToken = signup.GetProperty("signupToken").GetString()!;
        secrets.Add(("signup token", signupToken));
        Assert.Equal(HttpStatusCode.OK, (await website.SendAsync(Json(HttpMethod.Get, $"/signups/{signup.GetProperty("signupId").GetGuid()}", null, signupToken))).StatusCode);

        // 2. The owner verifies the email and chooses a password.
        var link = LinkSentTo(_app.Emails, email, "/Account/VerifyEmail");
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://x" + link).Query);
        secrets.Add(("verification code", query["code"]!));
        using var browser = _app.CreateClient();
        var page = new HtmlParser().ParseDocument(await (await browser.GetAsync(link)).Content.ReadAsStringAsync());
        var verified = await browser.PostAsync("/Account/VerifyEmail", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Signup"] = query["signup"]!, ["Code"] = query["code"]!, ["Password"] = password, ["ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = page.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!,
        }));
        Assert.Contains("data-testid=\"done\"", await verified.Content.ReadAsStringAsync());

        // 3. Sign-in: a wrong password, then the right one.
        using (var wrong = await SignIn.PostLoginAsync(_app.CreateClient(), email, wrongPassword)) { }
        using var owner = _app.CreateClient();
        await SignIn.AsAsync(owner, email, password);

        // 4. A billing link and its token.
        Assert.Equal(HttpStatusCode.Accepted, (await website.SendAsync(Json(HttpMethod.Post, "/billing-access/requests", new { email, captchaToken = FakeCaptchaVerifier.Pass }))).StatusCode);
        await ((InlineBillingAccessQueue)_app.Services.GetRequiredService<Inprem.Platform.Application.PublicApi.IBillingAccessQueue>()).RunAllAsync();
        var code = Uri.UnescapeDataString(LinkSentTo(_app.Emails, email, "/billing?code=").Split("code=")[1]);
        secrets.Add(("billing code", code));
        var session = await website.SendAsync(Json(HttpMethod.Post, "/billing-access/sessions", new { code }));
        Assert.Equal(HttpStatusCode.Created, session.StatusCode);
        var billingToken = JsonDocument.Parse(await session.Content.ReadAsStringAsync()).RootElement.GetProperty("billingAccessToken").GetString()!;
        secrets.Add(("billing token", billingToken));
        Assert.Equal(HttpStatusCode.OK, (await website.SendAsync(Json(HttpMethod.Get, "/account/subscription", null, billingToken))).StatusCode);

        // 5. A PayPal webhook with a payer's email and other payload.
        var payer = $"payer-{unique}@buyer.test";
        var payloadMarker = $"PAYLOAD-{unique}";
        secrets.Add(("PayPal payer email", payer));
        secrets.Add(("PayPal payload", payloadMarker));
        var paypal = new HttpRequestMessage(HttpMethod.Post, "/webhooks/paypal")
        {
            Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["id"] = $"WH-{unique}", ["event_type"] = "BILLING.SUBSCRIPTION.ACTIVATED", ["resource_type"] = "subscription", ["create_time"] = "2026-10-22T10:00:00Z",
                ["resource"] = new Dictionary<string, object> { ["id"] = $"I-{unique}", ["status"] = "ACTIVE", ["custom_id"] = payloadMarker, ["subscriber"] = new { email_address = payer } },
            }), Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in new[] { ("PAYPAL-AUTH-ALGO", "SHA256withRSA"), ("PAYPAL-CERT-URL", "https://api.sandbox.paypal.com/v1/notifications/certs/CERT"),
                     ("PAYPAL-TRANSMISSION-ID", Guid.NewGuid().ToString()), ("PAYPAL-TRANSMISSION-SIG", "sig"), ("PAYPAL-TRANSMISSION-TIME", "2026-10-22T10:00:00Z") })
            paypal.Headers.Add(name, value);
        Assert.Equal(HttpStatusCode.OK, (await _app.CreateClient().SendAsync(paypal)).StatusCode);

        // 6. An incoming text (a question, then STOP) from the owner's phone.
        var textBody = $"Hello there {unique}";
        secrets.Add(("text body", textBody));
        foreach (var body in new[] { textBody, "STOP" })
        {
            var fields = new Dictionary<string, string> { ["From"] = mobile, ["To"] = "+18005550000", ["Body"] = body, ["MessageSid"] = "SM" + unique };
            var inbound = new HttpRequestMessage(HttpMethod.Post, "/webhooks/twilio/inbound") { Content = new FormUrlEncodedContent(fields) };
            inbound.Headers.Add("X-Twilio-Signature", TwilioSignature.Compute(TwilioToken, WebhookBase + "/webhooks/twilio/inbound", fields));
            Assert.Equal(HttpStatusCode.OK, (await _app.CreateClient().SendAsync(inbound)).StatusCode);
        }

        // The check is only worth something if the flows logged.
        var entries = _logs.Entries.ToList();
        Assert.True(entries.Count > 20, $"Only {entries.Count} log lines captured; the capture looks broken.");
        Assert.Contains(entries, e => e.Category.StartsWith("Inprem", StringComparison.Ordinal));

        var leaks = (from e in entries
                     from s in secrets
                     where e.AllText.Contains(s.Value, StringComparison.OrdinalIgnoreCase)
                     select $"{s.What} in [{e.Level}] {e.Category}: {Shorten(e.Message)}").Distinct().ToList();
        Assert.True(leaks.Count == 0, "Found in the logs:\n" + string.Join("\n", leaks));

        // And the search finds a leak when there is one (a structured value, as a careless log call would write it).
        _app.Services.GetRequiredService<ILogger<LogScrubbingTests>>().LogInformation("Probe for {Account}.", email);
        Assert.Contains(_logs.Entries, e => e.AllText.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    private static string Shorten(string message) => message.Length > 200 ? message[..200] + "…" : message;
}
