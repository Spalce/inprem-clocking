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
using Xunit;

namespace Inprem.Platform.Tests.Integration.PayPal;

/// <summary>
/// Phase 4 step 4.1: the PayPal Invoicing v2 and refund client against a pretend PayPal (no
/// network): what is sent, how answers are read, which calls are retried (only those PayPal makes
/// safe), a repeated create finding the first invoice, and that payloads never reach the logs.
/// </summary>
public class PayPalInvoiceClientTests
{
    private const string PayerEmail = "treasurer-secret@centre.test";
    private const string InvoiceId = "INV2-Z56S-5LLA-Q52L-CPZ5";
    private const string PayerPage = "https://www.sandbox.paypal.com/invoice/p/#Z56S5LLAQ52LCPZ5";

    private sealed record Seen(HttpMethod Method, string Path, string Query, Dictionary<string, string> Headers, string Body);

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

        public List<Seen> To(string path) => Requests.Where(r => r.Path == path).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Enqueue(new Seen(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, headers, body));

            var route = $"{request.Method} {request.RequestUri.AbsolutePath}";
            if (route == "POST /v1/oauth2/token") return Json(HttpStatusCode.OK, """{"access_token":"A21-token","token_type":"Bearer","expires_in":32400}""");
            if (!_routes.TryGetValue(route, out var queue) || queue.Count == 0) return Json(HttpStatusCode.NotFound, """{"name":"RESOURCE_NOT_FOUND"}""");
            var (status, text) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return Json(status, text);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.Add("PayPal-Debug-Id", "f00dcafe5678");
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

    private static (IInvoiceProvider Invoicing, FakePayPal Fake, CapturedLogs Logs, ServiceProvider Services) Create()
    {
        var fake = new FakePayPal();
        var logs = new CapturedLogs();
        var values = new Dictionary<string, string?>
        {
            ["PayPal:ClientId"] = "test-client", ["PayPal:Secret"] = "test-secret", ["PayPal:WebhookId"] = "WH-OURS",
            ["PayPal:ReturnUrlAllowList:0"] = "https://app.inprem.test", ["PayPal:PlanReturnUrl"] = "https://app.inprem.test/Billing/PayPalReturn",
            ["PayPal:TimeoutSeconds"] = "5",
        };
        var services = new ServiceCollection()
            .AddLogging(l => l.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace))
            .AddPayPal(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddHttpClient(PayPalPaymentProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IInvoiceProvider>(), fake, logs, provider);
    }

    private static NewPayPalInvoice Draft(string number = "INV-100007") => new(
        number, "USD", new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 30), PayerEmail, "Ama", "Owusu", "Akim Asafo Community Centre",
        new[] { new PayPalInvoiceLine("Setup and training", 1m, 150m), new PayPalInvoiceLine("Extra kiosk (months)", 2.5m, 10m) },
        "Thank you for choosing Inprem.", "PO-12");

    private const string InvoiceJson = $$$"""
        {"id":"{{{InvoiceId}}}","status":"PARTIALLY_PAID",
         "detail":{"invoice_number":"INV-100007","currency_code":"USD","payment_term":{"term_type":"DUE_ON_DATE_SPECIFIED","due_date":"2026-10-30"},
                   "metadata":{"recipient_view_url":"{{{PayerPage}}}","invoicer_view_url":"https://www.sandbox.paypal.com/invoice/details/{{{InvoiceId}}}"}},
         "primary_recipients":[{"billing_info":{"email_address":"{{{PayerEmail}}}"}}],
         "amount":{"currency_code":"USD","value":"175.00"},
         "payments":{"paid_amount":{"currency_code":"USD","value":"100.00"},
                     "transactions":[{"payment_id":"7MN31234XL5678901","type":"PAYPAL","payment_date":"2026-10-17","amount":{"currency_code":"USD","value":"60.00"}},
                                     {"payment_id":"EXTR-86F38350LX4353815","type":"EXTERNAL","method":"BANK_TRANSFER","payment_date":"2026-10-18","amount":{"currency_code":"USD","value":"40.00"}}]},
         "refunds":{"refund_amount":{"currency_code":"USD","value":"10.00"},
                    "transactions":[{"refund_id":"EXTR-R-1","type":"EXTERNAL","refund_date":"2026-10-19","amount":{"currency_code":"USD","value":"10.00"}}]}}
        """;

    [Fact]
    public async Task A_Draft_Carries_Our_Number_Dates_Recipient_And_Lines()
    {
        var (invoicing, fake, logs, sp) = Create();
        using var _ = sp;
        fake.Answer("POST /v2/invoicing/invoices", HttpStatusCode.Created, InvoiceJson.Replace("PARTIALLY_PAID", "DRAFT"));

        Assert.Equal(InvoiceId, await invoicing.CreateDraftAsync(Draft()));

        var sent = fake.To("/v2/invoicing/invoices").Single();
        Assert.Equal("return=representation", sent.Headers["Prefer"]);
        var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal("INV-100007", body["detail"]!["invoice_number"]!.GetValue<string>());
        Assert.Equal("2026-10-16", body["detail"]!["invoice_date"]!.GetValue<string>());
        Assert.Equal("DUE_ON_DATE_SPECIFIED", body["detail"]!["payment_term"]!["term_type"]!.GetValue<string>());
        Assert.Equal("2026-10-30", body["detail"]!["payment_term"]!["due_date"]!.GetValue<string>());
        Assert.Equal("PO-12", body["detail"]!["reference"]!.GetValue<string>());
        var billing = body["primary_recipients"]![0]!["billing_info"]!;
        Assert.Equal((PayerEmail, "Ama", "Owusu", "Akim Asafo Community Centre"),
            (billing["email_address"]!.GetValue<string>(), billing["name"]!["given_name"]!.GetValue<string>(), billing["name"]!["surname"]!.GetValue<string>(), billing["business_name"]!.GetValue<string>()));
        var items = body["items"]!.AsArray();
        Assert.Equal(("Extra kiosk (months)", "2.5", "10.00", "USD"),
            (items[1]!["name"]!.GetValue<string>(), items[1]!["quantity"]!.GetValue<string>(), items[1]!["unit_amount"]!["value"]!.GetValue<string>(), items[1]!["unit_amount"]!["currency_code"]!.GetValue<string>()));
        Assert.False(body["configuration"]!["partial_payment"]!["allow_partial_payment"]!.GetValue<bool>());

        Assert.DoesNotContain(logs.Lines, l => l.Contains(PayerEmail) || l.Contains("Owusu") || l.Contains("Akim Asafo"));
    }

    [Fact]
    public async Task A_Repeated_Create_Finds_The_Invoice_PayPal_Already_Has()
    {
        var (invoicing, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v2/invoicing/invoices", HttpStatusCode.UnprocessableEntity,
            """{"name":"UNPROCESSABLE_ENTITY","message":"The requested action could not be performed.","details":[{"field":"/detail/invoice_number","value":"INV-100007","issue":"DUPLICATE_INVOICE_NUMBER"}]}""");
        fake.Answer("POST /v2/invoicing/search-invoices", HttpStatusCode.OK, $$"""{"total_items":1,"items":[{{InvoiceJson}}]}""");

        Assert.Equal(InvoiceId, await invoicing.CreateDraftAsync(Draft()));
        Assert.Equal("INV-100007", JsonNode.Parse(fake.To("/v2/invoicing/search-invoices").Single().Body)!["invoice_number"]!.GetValue<string>());
    }

    [Fact]
    public async Task Calls_That_Cannot_Be_Repeated_Safely_Are_Not_Retried_Refunds_Are()
    {
        var (invoicing, fake, _, sp) = Create();
        using var __ = sp;

        // Create, send and recorded payments carry no PayPal-Request-Id: one attempt only.
        fake.Answer("POST /v2/invoicing/invoices", HttpStatusCode.ServiceUnavailable, """{"name":"SERVICE_UNAVAILABLE"}""");
        var e = await Assert.ThrowsAsync<PaymentProviderException>(() => invoicing.CreateDraftAsync(Draft()));
        Assert.Equal((503, "f00dcafe5678", "SERVICE_UNAVAILABLE"), (e.StatusCode, e.DebugId, e.Issue));
        Assert.Single(fake.To("/v2/invoicing/invoices"));

        fake.Answer($"POST /v2/invoicing/invoices/{InvoiceId}/payments", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<PaymentProviderException>(() => invoicing.RecordExternalPaymentAsync(InvoiceId, new DateOnly(2026, 10, 18), OfflinePaymentMethod.Cash, 40m, "USD", null));
        Assert.Single(fake.To($"/v2/invoicing/invoices/{InvoiceId}/payments"));

        // A refund carries our request id, so PayPal refunds once: retried.
        fake.Answer("POST /v2/payments/captures/7MN31234XL5678901/refund", HttpStatusCode.ServiceUnavailable, "{}")
            .Answer("POST /v2/payments/captures/7MN31234XL5678901/refund", HttpStatusCode.Created, """{"id":"1JU08902781691411","status":"COMPLETED"}""");
        var refund = await invoicing.RefundAsync(RefundTarget.InvoiceCapture, "7MN31234XL5678901", 25m, "USD", "refund-req-1", "Duplicate payment");
        Assert.Equal(new ProviderRefund("1JU08902781691411", "COMPLETED"), refund);
        var attempts = fake.To("/v2/payments/captures/7MN31234XL5678901/refund");
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, a => Assert.Equal("refund-req-1", a.Headers["PayPal-Request-Id"]));
        var body = JsonNode.Parse(attempts[0].Body)!;
        Assert.Equal(("25.00", "USD", "Duplicate payment"), (body["amount"]!["value"]!.GetValue<string>(), body["amount"]!["currency_code"]!.GetValue<string>(), body["note_to_payer"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_Subscription_Payment_Is_Refunded_As_A_PayPal_Sale()
    {
        var (invoicing, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer("POST /v1/payments/sale/80021663DE681814L/refund", HttpStatusCode.Created, """{"id":"0P209507D6694645N","state":"completed"}""");

        var refund = await invoicing.RefundAsync(RefundTarget.SubscriptionSale, "80021663DE681814L", 29m, "USD", "refund-req-2", null);

        Assert.Equal(new ProviderRefund("0P209507D6694645N", "COMPLETED"), refund);
        var body = JsonNode.Parse(fake.To("/v1/payments/sale/80021663DE681814L/refund").Single().Body)!;
        Assert.Equal(("29.00", "USD"), (body["amount"]!["total"]!.GetValue<string>(), body["amount"]!["currency"]!.GetValue<string>()));
        await Assert.ThrowsAsync<ArgumentException>(() => invoicing.RefundAsync(RefundTarget.SubscriptionSale, "../../v1/other", 1m, "USD", "r", null));
        await Assert.ThrowsAsync<ArgumentException>(() => invoicing.RefundAsync(RefundTarget.SubscriptionSale, "80021663DE681814L", 1m, "USD", " ", null));
    }

    [Fact]
    public async Task Send_Returns_The_Payer_Page_Also_When_It_Was_Sent_Before()
    {
        var (invoicing, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer($"POST /v2/invoicing/invoices/{InvoiceId}/send", HttpStatusCode.OK, $$"""{"href":"{{PayerPage}}","rel":"payer-view","method":"GET"}""");
        Assert.Equal(PayerPage, await invoicing.SendAsync(InvoiceId));
        var sent = JsonNode.Parse(fake.To($"/v2/invoicing/invoices/{InvoiceId}/send").Single().Body)!;
        Assert.True(sent["send_to_recipient"]!.GetValue<bool>());

        // Our first attempt's answer was lost: PayPal says it is sent already; the page comes from the invoice.
        var (again, fake2, _, sp2) = Create();
        using var ___ = sp2;
        fake2.Answer($"POST /v2/invoicing/invoices/{InvoiceId}/send", HttpStatusCode.UnprocessableEntity, """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"INVOICE_ALREADY_SENT"}]}""")
            .Answer($"GET /v2/invoicing/invoices/{InvoiceId}", HttpStatusCode.OK, InvoiceJson);
        Assert.Equal(PayerPage, await again.SendAsync(InvoiceId));
    }

    [Fact]
    public async Task Get_Reads_Status_Amounts_Payments_And_Refunds()
    {
        var (invoicing, fake, _, sp) = Create();
        using var __ = sp;
        fake.Answer($"GET /v2/invoicing/invoices/{InvoiceId}", HttpStatusCode.OK, InvoiceJson);

        var info = (await invoicing.GetAsync(InvoiceId))!;
        Assert.Equal((InvoiceId, "INV-100007", PayPalInvoiceState.PartiallyPaid, "PARTIALLY_PAID"), (info.Id, info.Number, info.State, info.RawStatus));
        Assert.Equal((175m, "USD", 100m, 10m, new DateOnly(2026, 10, 30), PayerPage), (info.Total, info.Currency, info.PaidAmount, info.RefundedAmount, info.DueDate, info.PayerUrl));
        Assert.Equal(new[] { ("7MN31234XL5678901", false, 60m), ("EXTR-86F38350LX4353815", true, 40m) }, info.Payments.Select(p => (p.PaymentId!, p.External, p.Amount)));
        Assert.Equal("BANK_TRANSFER", info.Payments[1].Method);
        Assert.Equal(("EXTR-R-1", true, 10m), (info.Refunds[0].RefundId!, info.Refunds[0].External, info.Refunds[0].Amount));

        Assert.Null(await invoicing.GetAsync("INV2-UNKNOWN-0000-0000-0000"));
    }

    [Fact]
    public async Task Cancel_Deletes_A_Draft_Cancels_A_Sent_Invoice_And_Accepts_A_Cancelled_One()
    {
        var (invoicing, fake, _, sp) = Create();
        using var __ = sp;
        var path = $"/v2/invoicing/invoices/{InvoiceId}";

        fake.Answer($"GET {path}", HttpStatusCode.OK, InvoiceJson.Replace("PARTIALLY_PAID", "DRAFT"))
            .Answer($"GET {path}", HttpStatusCode.OK, InvoiceJson.Replace("PARTIALLY_PAID", "SENT"))
            .Answer($"GET {path}", HttpStatusCode.OK, InvoiceJson.Replace("PARTIALLY_PAID", "CANCELLED"));
        fake.Answer($"DELETE {path}", HttpStatusCode.NoContent, "");
        fake.Answer($"POST {path}/cancel", HttpStatusCode.NoContent, "");

        await invoicing.CancelAsync(InvoiceId, "Sent by mistake");
        Assert.Single(fake.Requests, r => r.Method == HttpMethod.Delete);
        await invoicing.CancelAsync(InvoiceId, "Sent by mistake");
        Assert.Equal("Sent by mistake", JsonNode.Parse(fake.To(path + "/cancel").Single().Body)!["note"]!.GetValue<string>());
        await invoicing.CancelAsync(InvoiceId, "again");
        Assert.Single(fake.To(path + "/cancel")); // already cancelled: nothing more is sent
    }

    [Fact]
    public async Task Offline_Payments_And_Refunds_Are_Recorded_With_PayPal_Methods()
    {
        var (invoicing, fake, logs, sp) = Create();
        using var __ = sp;
        fake.Answer($"POST /v2/invoicing/invoices/{InvoiceId}/payments", HttpStatusCode.OK, """{"payment_id":"EXTR-86F38350LX4353815"}""");
        fake.Answer($"POST /v2/invoicing/invoices/{InvoiceId}/refunds", HttpStatusCode.OK, """{"refund_id":"EXTR-R-2"}""");

        Assert.Equal("EXTR-86F38350LX4353815", await invoicing.RecordExternalPaymentAsync(InvoiceId, new DateOnly(2026, 10, 18), OfflinePaymentMethod.MobileMoney, 40m, "USD", "MTN MoMo ref 5521"));
        Assert.Equal("EXTR-R-2", await invoicing.RecordExternalRefundAsync(InvoiceId, new DateOnly(2026, 10, 19), OfflinePaymentMethod.Cheque, 10m, "USD"));

        var payment = JsonNode.Parse(fake.To($"/v2/invoicing/invoices/{InvoiceId}/payments").Single().Body)!;
        Assert.Equal(("OTHER", "2026-10-18", "40.00"), (payment["method"]!.GetValue<string>(), payment["payment_date"]!.GetValue<string>(), payment["amount"]!["value"]!.GetValue<string>()));
        Assert.Equal("CHECK", JsonNode.Parse(fake.To($"/v2/invoicing/invoices/{InvoiceId}/refunds").Single().Body)!["method"]!.GetValue<string>());
        Assert.DoesNotContain(logs.Lines, l => l.Contains("MoMo"));
    }

    [Fact]
    public async Task Transaction_Search_Reads_Every_Page_For_The_Month()
    {
        var (invoicing, fake, logs, sp) = Create();
        using var _ = sp;
        fake.Answer("GET /v1/reporting/transactions", HttpStatusCode.OK, """
            {"transaction_details":[{"transaction_info":{"transaction_id":"8MC585209K746392H","transaction_event_code":"T0002","transaction_initiation_date":"2026-09-03T10:00:00+0000",
               "transaction_amount":{"currency_code":"USD","value":"29.00"},"transaction_status":"S"},"payer_info":{"email_address":"treasurer-secret@centre.test"}}],
             "total_pages":2,"page":1}
            """).Answer("GET /v1/reporting/transactions", HttpStatusCode.OK, """
            {"transaction_details":[{"transaction_info":{"transaction_id":"1JU08902781691411","transaction_event_code":"T1107","transaction_initiation_date":"2026-09-06T10:00:00+0000",
               "transaction_amount":{"currency_code":"USD","value":"-10.00"},"transaction_status":"S","invoice_id":"INV-100007"}}],
             "total_pages":2,"page":2}
            """);

        var found = await invoicing.SearchTransactionsAsync(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new[] { ("8MC585209K746392H", "T0002", 29m), ("1JU08902781691411", "T1107", -10m) }, found.Select(t => (t.Id, t.EventCode!, t.Amount)));
        Assert.Equal((new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc), "INV-100007"), (found[1].DateUtc, found[1].InvoiceId));
        var asked = fake.To("/v1/reporting/transactions");
        Assert.Equal(2, asked.Count);
        Assert.Contains("start_date=2026-09-01T00%3A00%3A00Z", asked[0].Query);
        Assert.Contains("end_date=2026-09-30T23%3A59%3A59Z", asked[0].Query);
        Assert.Contains("balance_affecting_records_only=Y", asked[0].Query);
        Assert.Contains("page=2", asked[1].Query);
        Assert.DoesNotContain(logs.Lines, l => l.Contains("treasurer-secret"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => invoicing.SearchTransactionsAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData("DRAFT", PayPalInvoiceState.Draft)]
    [InlineData("SCHEDULED", PayPalInvoiceState.Scheduled)]
    [InlineData("SENT", PayPalInvoiceState.Sent)]
    [InlineData("UNPAID", PayPalInvoiceState.Sent)]
    [InlineData("SHARED", PayPalInvoiceState.Sent)]
    [InlineData("PAYMENT_PENDING", PayPalInvoiceState.PaymentPending)]
    [InlineData("PARTIALLY_PAID", PayPalInvoiceState.PartiallyPaid)]
    [InlineData("PAID", PayPalInvoiceState.Paid)]
    [InlineData("MARKED_AS_PAID", PayPalInvoiceState.Paid)]
    [InlineData("PAID_EXTERNAL", PayPalInvoiceState.Paid)]
    [InlineData("PARTIALLY_REFUNDED", PayPalInvoiceState.PartiallyRefunded)]
    [InlineData("REFUNDED", PayPalInvoiceState.Refunded)]
    [InlineData("MARKED_AS_REFUNDED", PayPalInvoiceState.Refunded)]
    [InlineData("REFUNDED_EXTERNAL", PayPalInvoiceState.Refunded)]
    [InlineData("CANCELLED", PayPalInvoiceState.Cancelled)]
    [InlineData("AUTO_CANCELLED", PayPalInvoiceState.Cancelled)]
    [InlineData("SOMETHING_NEW", PayPalInvoiceState.Unknown)]
    public void Every_PayPal_Status_Is_Grouped(string status, PayPalInvoiceState expected) => Assert.Equal(expected, PayPalInvoiceProvider.State(status));

    [Fact]
    public async Task Not_Configured_Says_So_And_Calls_Nothing()
    {
        var fake = new FakePayPal();
        var services = new ServiceCollection().AddLogging()
            .AddPayPal(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
        services.AddHttpClient(PayPalPaymentProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        using var sp = services.BuildServiceProvider();
        var invoicing = sp.GetRequiredService<IInvoiceProvider>();

        Assert.False(invoicing.IsConfigured);
        await Assert.ThrowsAsync<PaymentProviderException>(() => invoicing.CreateDraftAsync(Draft()));
        Assert.Empty(fake.Requests);
    }
}
