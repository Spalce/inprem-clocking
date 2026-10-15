using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inprem.Platform.Tests.Integration.PayPal;

/// <summary>
/// Phase 2 step 2.2: the PayPal client against a pretend PayPal (no network): what is sent, how
/// answers are read, token caching, retries, webhook verification, configuration checks, and that
/// payloads never reach the logs.
/// </summary>
public class PayPalClientTests
{
    private const string Origin = "https://app.inprem.test";
    private const string PayerEmail = "payer-secret@buyer.test";

    // ------------------------------------------------------------------ setup

    private sealed record Seen(HttpMethod Method, string Path, Dictionary<string, string> Headers, string Body);

    /// <summary>Answers by "METHOD /path"; a queue per route lets a test script a failure then a success.</summary>
    private sealed class FakePayPal : HttpMessageHandler
    {
        public ConcurrentQueue<Seen> Requests { get; } = new();
        private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _routes = new();

        public FakePayPal Answer(string route, HttpStatusCode status, string body)
        {
            if (!_routes.TryGetValue(route, out var queue)) _routes[route] = queue = new();
            queue.Enqueue((status, body));
            return this;
        }

        public IEnumerable<Seen> To(string path) => Requests.Where(r => r.Path == path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Enqueue(new Seen(request.Method, request.RequestUri!.AbsolutePath, headers, body));

            var route = $"{request.Method} {request.RequestUri.AbsolutePath}";
            if (route == "POST /v1/oauth2/token" && !_routes.ContainsKey(route))
                return Json(HttpStatusCode.OK, """{"access_token":"A21-token","token_type":"Bearer","expires_in":32400}""");
            if (!_routes.TryGetValue(route, out var queue) || queue.Count == 0) return Json(HttpStatusCode.NotFound, """{"name":"RESOURCE_NOT_FOUND"}""");
            var (status, text) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return Json(status, text);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.Add("PayPal-Debug-Id", "f00dcafe1234");
            return response;
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger : ILogger
        {
            private readonly CapturedLogs _owner;
            public Logger(CapturedLogs owner) => _owner = owner;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _owner.Lines.Enqueue(formatter(state, exception) + (exception == null ? "" : " " + exception));
        }
    }

    private static (IPaymentProvider PayPal, FakePayPal Fake, CapturedLogs Logs, ServiceProvider Services) Create(Dictionary<string, string?>? settings = null, FakePayPal? fake = null)
    {
        fake ??= new FakePayPal();
        var (provider, logs) = Services(settings, fake);
        return (provider.GetRequiredService<IPaymentProvider>(), fake, logs, provider);
    }

    private static (ServiceProvider Provider, CapturedLogs Logs) Services(Dictionary<string, string?>? settings, FakePayPal fake)
    {
        var values = new Dictionary<string, string?>
        {
            ["PayPal:ClientId"] = "test-client", ["PayPal:Secret"] = "test-secret", ["PayPal:WebhookId"] = "WH-OURS",
            ["PayPal:ReturnUrlAllowList:0"] = Origin, ["PayPal:PlanReturnUrl"] = Origin + "/Billing/PayPalReturn", ["PayPal:PlanCancelUrl"] = Origin + "/Billing",
            ["PayPal:TimeoutSeconds"] = "5",
        };
        foreach (var (k, v) in settings ?? new()) values[k] = v;
        var logs = new CapturedLogs();
        var services = new ServiceCollection()
            .AddLogging(l => l.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace))
            .AddPayPal(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddHttpClient(PayPalPaymentProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        return (services.BuildServiceProvider(), logs);
    }

    private const string CreatedJson = """
        {"id":"I-BW452GLLEP1G","status":"APPROVAL_PENDING","plan_id":"P-5ML4271244454362WXNWU5NQ","custom_id":"chk_1",
         "subscriber":{"email_address":"payer-secret@buyer.test"},
         "links":[{"href":"https://www.sandbox.paypal.com/webapps/billing/subscriptions?ba_token=BA-2M539689T3856352J","rel":"approve","method":"GET"},
                  {"href":"https://api-m.sandbox.paypal.com/v1/billing/subscriptions/I-BW452GLLEP1G","rel":"self","method":"GET"}]}
        """;

    // ------------------------------------------------------------ subscriptions

    [Fact]
    public async Task Create_Subscription_Sends_Our_Reference_Addresses_And_Request_Id()
    {
        var (paypal, fake, logs, sp) = Create();
        using var _ = sp;
        fake.Answer("POST /v1/billing/subscriptions", HttpStatusCode.Created, CreatedJson);

        var created = await paypal.CreateSubscriptionAsync("P-5ML4271244454362WXNWU5NQ", "chk_1", Origin + "/Billing/Return?s=1", Origin + "/Billing", "req-1");

        Assert.Equal(("I-BW452GLLEP1G", PayPalSubscriptionState.ApprovalPending), (created.Id, created.State));
        Assert.StartsWith("https://www.sandbox.paypal.com/webapps/billing/subscriptions?ba_token=", created.ApprovalUrl);

        var sent = fake.To("/v1/billing/subscriptions").Single();
        var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal(("P-5ML4271244454362WXNWU5NQ", "chk_1"), (body["plan_id"]!.GetValue<string>(), body["custom_id"]!.GetValue<string>()));
        Assert.Equal(Origin + "/Billing/Return?s=1", body["application_context"]!["return_url"]!.GetValue<string>());
        Assert.Equal(Origin + "/Billing", body["application_context"]!["cancel_url"]!.GetValue<string>());
        Assert.Equal("req-1", sent.Headers["PayPal-Request-Id"]);
        Assert.Equal("Bearer A21-token", sent.Headers["Authorization"]);

        // The token request uses the app's credentials (Basic), and is the only place they appear.
        var token = fake.To("/v1/oauth2/token").First();
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-client:test-secret")), token.Headers["Authorization"]);

        // Logs: the call with its correlation id, but never the payload or the payer.
        Assert.Contains(logs.Lines, l => l.Contains("/v1/billing/subscriptions") && l.Contains("f00dcafe1234"));
        var leaks = logs.Lines.Where(l => l.Contains(PayerEmail) || l.Contains("chk_1") || l.Contains("test-secret") || l.Contains("A21-token") || l.Contains("Basic ")).ToList();
        Assert.True(leaks.Count == 0, "Logged: " + string.Join(" || ", leaks));
    }

    [Fact]
    public async Task Return_And_Cancel_Addresses_Must_Be_On_The_Allow_List()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        Assert.True(paypal.IsAllowedReturnUrl(Origin + "/x?y=1"));
        Assert.False(paypal.IsAllowedReturnUrl("http://app.inprem.test/x"));
        Assert.False(paypal.IsAllowedReturnUrl("https://evil.test/x"));
        Assert.False(paypal.IsAllowedReturnUrl("https://app.inprem.test.evil.test/x"));
        Assert.False(paypal.IsAllowedReturnUrl("https://user@app.inprem.test/x"));
        Assert.False(paypal.IsAllowedReturnUrl("https://app.inprem.test:8443/x"));
        Assert.False(paypal.IsAllowedReturnUrl("/Billing"));

        await Assert.ThrowsAsync<ArgumentException>(() => paypal.CreateSubscriptionAsync("P-1", "chk", "https://evil.test/r", Origin + "/c", "req"));
        Assert.Empty(fake.To("/v1/billing/subscriptions"));
    }

    [Fact]
    public async Task Get_Subscription_Reads_Status_Period_And_Balance()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("GET /v1/billing/subscriptions/I-1", HttpStatusCode.OK, """
            {"id":"I-1","status":"ACTIVE","plan_id":"P-5ML4271244454362WXNWU5NQ","custom_id":"chk_9","start_time":"2026-10-15T10:00:00Z",
             "billing_info":{"next_billing_time":"2026-11-15T10:00:00Z","failed_payments_count":2,
               "outstanding_balance":{"currency_code":"USD","value":"29.00"},
               "last_payment":{"amount":{"currency_code":"USD","value":"29.00"},"time":"2026-10-15T10:01:00Z"},
               "cycle_executions":[]}}
            """);

        var s = await paypal.GetSubscriptionAsync("I-1");

        Assert.NotNull(s);
        Assert.Equal((PayPalSubscriptionState.Active, "P-5ML4271244454362WXNWU5NQ", "chk_9"), (s!.State, s.PlanId, s.CustomId));
        Assert.Equal(new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc), s.NextBillingUtc);
        Assert.Equal(DateTimeKind.Utc, s.NextBillingUtc!.Value.Kind);
        Assert.Equal((29m, "USD", 29m, 2), (s.LastPaymentAmount!.Value, s.LastPaymentCurrency, s.OutstandingBalance, s.FailedPaymentsCount));

        Assert.Null(await paypal.GetSubscriptionAsync("I-UNKNOWN")); // PayPal 404
    }

    [Fact]
    public async Task Transient_Failures_Are_Retried_With_The_Same_Request_Id()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/billing/subscriptions", HttpStatusCode.ServiceUnavailable, """{"name":"SERVICE_UNAVAILABLE"}""")
            .Answer("POST /v1/billing/subscriptions", HttpStatusCode.Created, CreatedJson);

        var created = await paypal.CreateSubscriptionAsync("P-5ML4271244454362WXNWU5NQ", "chk_1", Origin + "/r", Origin + "/c", "req-7");

        Assert.Equal("I-BW452GLLEP1G", created.Id);
        var attempts = fake.To("/v1/billing/subscriptions").ToList();
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, a => Assert.Equal("req-7", a.Headers["PayPal-Request-Id"]));
    }

    [Fact]
    public async Task Refusals_Become_A_Payment_Provider_Error_With_The_Debug_Id()
    {
        var (paypal, fake, logs, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/billing/subscriptions", HttpStatusCode.UnprocessableEntity,
            """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"PLAN_NOT_ACTIVE","description":"payer-secret@buyer.test"}]}""");

        var e = await Assert.ThrowsAsync<PaymentProviderException>(() => paypal.CreateSubscriptionAsync("P-1", "chk", Origin + "/r", Origin + "/c", "req"));

        Assert.Equal(422, e.StatusCode);
        Assert.Single(fake.To("/v1/billing/subscriptions")); // 4xx is not retried
        Assert.DoesNotContain(PayerEmail, e.Message);
        Assert.DoesNotContain(logs.Lines, l => l.Contains(PayerEmail));
    }

    [Fact]
    public async Task Cancel_Accepts_An_Already_Cancelled_Subscription()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/billing/subscriptions/I-1/cancel", HttpStatusCode.NoContent, "");
        await paypal.CancelSubscriptionAsync("I-1", "Owner cancelled");
        Assert.Contains("Owner cancelled", fake.To("/v1/billing/subscriptions/I-1/cancel").Single().Body);

        fake.Answer("POST /v1/billing/subscriptions/I-2/cancel", HttpStatusCode.UnprocessableEntity, """{"name":"UNPROCESSABLE_ENTITY"}""")
            .Answer("GET /v1/billing/subscriptions/I-2", HttpStatusCode.OK, """{"id":"I-2","status":"CANCELLED"}""");
        await paypal.CancelSubscriptionAsync("I-2", "again");

        fake.Answer("POST /v1/billing/subscriptions/I-3/cancel", HttpStatusCode.UnprocessableEntity, """{"name":"UNPROCESSABLE_ENTITY"}""")
            .Answer("GET /v1/billing/subscriptions/I-3", HttpStatusCode.OK, """{"id":"I-3","status":"ACTIVE"}""");
        await Assert.ThrowsAsync<PaymentProviderException>(() => paypal.CancelSubscriptionAsync("I-3", "x"));
    }

    [Fact]
    public async Task Revise_Returns_The_Approve_Link()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/billing/subscriptions/I-1/revise", HttpStatusCode.OK,
            """{"plan_id":"P-NEW","links":[{"href":"https://www.sandbox.paypal.com/approve-revision","rel":"approve","method":"GET"}]}""");

        Assert.Equal("https://www.sandbox.paypal.com/approve-revision", await paypal.ReviseSubscriptionAsync("I-1", "P-NEW", Origin + "/r", Origin + "/c"));
        Assert.Equal("P-NEW", JsonNode.Parse(fake.To("/v1/billing/subscriptions/I-1/revise").Single().Body)!["plan_id"]!.GetValue<string>());
    }

    // -------------------------------------------------------------- catalog

    [Fact]
    public async Task Product_And_Plan_Carry_Our_Billing_Rules()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/catalogs/products", HttpStatusCode.Created, """{"id":"PROD-XXCD1234QWER65782","name":"Standard"}""")
            .Answer("POST /v1/billing/plans", HttpStatusCode.Created, """{"id":"P-5ML4271244454362WXNWU5NQ","status":"ACTIVE"}""");

        Assert.Equal("PROD-XXCD1234QWER65782", await paypal.CreateProductAsync("Standard", "For small teams", "prod-req"));
        var product = fake.To("/v1/catalogs/products").Single();
        Assert.Equal(("prod-req", "SERVICE"), (product.Headers["PayPal-Request-Id"], JsonNode.Parse(product.Body)!["type"]!.GetValue<string>()));

        Assert.Equal("P-5ML4271244454362WXNWU5NQ", await paypal.CreatePlanAsync(new NewBillingPlan("PROD-XXCD1234QWER65782", "Standard monthly", null, BillingCycle.Monthly, 29m, "USD"), "plan-req"));
        var plan = JsonNode.Parse(fake.To("/v1/billing/plans").Single().Body)!;
        Assert.Equal("PROD-XXCD1234QWER65782", plan["product_id"]!.GetValue<string>());
        Assert.True(plan["payment_preferences"]!["auto_bill_outstanding"]!.GetValue<bool>());
        Assert.Equal(1, plan["payment_preferences"]!["payment_failure_threshold"]!.GetValue<int>()); // D23
        var cycle = plan["billing_cycles"]![0]!;
        Assert.Equal(("MONTH", "REGULAR", 0), (cycle["frequency"]!["interval_unit"]!.GetValue<string>(), cycle["tenure_type"]!.GetValue<string>(), cycle["total_cycles"]!.GetValue<int>()));
        Assert.Equal(("29.00", "USD"), (cycle["pricing_scheme"]!["fixed_price"]!["value"]!.GetValue<string>(), cycle["pricing_scheme"]!["fixed_price"]!["currency_code"]!.GetValue<string>()));
        Assert.Equal(Origin + "/Billing/PayPalReturn", plan["merchant_preferences"]!["return_url"]!.GetValue<string>()); // the fallback
    }

    // ------------------------------------------------------------- webhooks

    private static readonly Dictionary<string, string> GoodHeaders = new()
    {
        ["paypal-auth-algo"] = "SHA256withRSA", ["paypal-cert-url"] = "https://api.sandbox.paypal.com/v1/notifications/certs/CERT-360caa42",
        ["paypal-transmission-id"] = "69cd13f0-d67a-11e5-baa3-778b53f4ae55", ["paypal-transmission-sig"] = "lmI95Jx3Y9nhR5SJWlHVIWpg4AgFk7n9bCHSRxbrd8A9zrhdu2rMyFrmz+Zjh3s3boXB07VXCXUZy/UFzUlnGJn0wDugt7FlSvdKeIJenLRemUxYCPVoEZzg9VFNqOa48gMkvF+XTpxBeUx/kWy6B5cp7GkT2+pOowfRK7OaynuxUoKW3JcMWw272VKjLTtTAShncla7tGF+55rxyt2KNZIIqxNMJ48RDZheGU5w1npu9dZHnPgTXB9iomeVRoD8O/jhRpnKsGrDschyNdkeh81BJJMH4Ctc6lnCCquoP/GzCzz33MMsNdid7vL/NIWaCsekQpW26FpWPi/tfj8nLA==",
        ["paypal-transmission-time"] = "2026-10-15T08:00:00Z",
    };

    private const string EventJson = """{"id":"WH-2WR32451HC0233532-67976317FL4543714","event_type":"BILLING.SUBSCRIPTION.ACTIVATED","resource":{"id":"I-1","subscriber":{"email_address":"payer-secret@buyer.test"}}}""";

    [Fact]
    public async Task Webhook_Is_Verified_With_PayPal_Using_The_Raw_Event()
    {
        var (paypal, fake, logs, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/notifications/verify-webhook-signature", HttpStatusCode.OK, """{"verification_status":"SUCCESS"}""");

        Assert.True(await paypal.VerifyWebhookAsync(new WebhookMessage(GoodHeaders, EventJson)));

        var sent = JsonNode.Parse(fake.To("/v1/notifications/verify-webhook-signature").Single().Body)!;
        Assert.Equal("WH-OURS", sent["webhook_id"]!.GetValue<string>());
        Assert.Equal("69cd13f0-d67a-11e5-baa3-778b53f4ae55", sent["transmission_id"]!.GetValue<string>());
        Assert.Equal(JsonNode.Parse(EventJson)!.ToJsonString(), sent["webhook_event"]!.ToJsonString()); // the event as received
        Assert.DoesNotContain(logs.Lines, l => l.Contains(PayerEmail));
    }

    [Fact]
    public async Task Webhook_Refused_When_PayPal_Says_No_Or_Anything_Is_Missing()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/notifications/verify-webhook-signature", HttpStatusCode.OK, """{"verification_status":"FAILURE"}""");
        Assert.False(await paypal.VerifyWebhookAsync(new WebhookMessage(GoodHeaders, EventJson)));

        var calls = fake.To("/v1/notifications/verify-webhook-signature").Count();
        var missing = new Dictionary<string, string>(GoodHeaders);
        missing.Remove("paypal-transmission-sig");
        Assert.False(await paypal.VerifyWebhookAsync(new WebhookMessage(missing, EventJson)));
        Assert.False(await paypal.VerifyWebhookAsync(new WebhookMessage(GoodHeaders, "not json")));
        Assert.Equal(calls, fake.To("/v1/notifications/verify-webhook-signature").Count()); // PayPal not even asked

        var (noWebhookId, fake2, _, sp2) = Create(new() { ["PayPal:WebhookId"] = null });
        using var ___ = sp2;
        Assert.False(await noWebhookId.VerifyWebhookAsync(new WebhookMessage(GoodHeaders, EventJson)));
        Assert.Empty(fake2.Requests);

        var (down, fake3, _, sp3) = Create();
        using var ____ = sp3;
        fake3.Answer("POST /v1/notifications/verify-webhook-signature", HttpStatusCode.InternalServerError, "{}");
        Assert.False(await down.VerifyWebhookAsync(new WebhookMessage(GoodHeaders, EventJson))); // PayPal down: refused, PayPal redelivers
    }

    // -------------------------------------------------------- configuration

    [Fact]
    public async Task Tokens_Are_Cached()
    {
        var (paypal, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/catalogs/products", HttpStatusCode.Created, """{"id":"PROD-1"}""");
        await paypal.CreateProductAsync("A", null, "r1");
        await paypal.CreateProductAsync("B", null, "r2");
        Assert.Single(fake.To("/v1/oauth2/token"));
    }

    [Fact]
    public async Task Not_Configured_Says_So_And_Calls_Nothing()
    {
        var (paypal, fake, _, sp) = Create(new() { ["PayPal:ClientId"] = null, ["PayPal:Secret"] = null });
        using var __ = sp;
        Assert.False(paypal.IsConfigured);
        var e = await Assert.ThrowsAsync<PaymentProviderException>(() => paypal.GetSubscriptionAsync("I-1"));
        Assert.Contains("not configured", e.Message);
        Assert.False(await paypal.VerifyWebhookAsync(new WebhookMessage(GoodHeaders, EventJson)));
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData("PayPal:Mode", "Live", "AllowLive")]
    [InlineData("PayPal:Secret", null, "PayPal:Secret is missing")]
    [InlineData("PayPal:ReturnUrlAllowList:0", "http://app.inprem.test", "must be an https origin")]
    [InlineData("PayPal:ReturnUrlAllowList:0", "https://app.inprem.test/path", "must be an https origin")]
    [InlineData("PayPal:PlanReturnUrl", "https://elsewhere.test/r", "PlanReturnUrl")]
    [InlineData("PayPal:TimeoutSeconds", "1", "TimeoutSeconds")]
    public void Wrong_Configuration_Is_Refused_At_Startup(string key, string? value, string message)
    {
        var (sp, _) = Services(new() { [key] = value }, new FakePayPal());
        using var __ = sp;
        var e = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<PayPalOptions>>().Value);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void Live_Mode_Uses_The_Live_Address_Only_When_Allowed()
    {
        var (_, _, _, sp) = Create(new() { ["PayPal:Mode"] = "Live", ["PayPal:AllowLive"] = "true" });
        using var __ = sp;
        Assert.Equal("https://api-m.paypal.com/", sp.GetRequiredService<IOptions<PayPalOptions>>().Value.BaseAddress.ToString());
        Assert.Equal("https://api-m.sandbox.paypal.com/", new PayPalOptions().BaseAddress.ToString());
    }
}
