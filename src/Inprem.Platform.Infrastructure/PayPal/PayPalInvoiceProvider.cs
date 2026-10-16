using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inprem.Platform.Infrastructure.PayPal;

/// <summary>
/// PayPal Invoicing v2 and refunds (Phase 4 step 4.1) over the shared <c>PayPal</c> HttpClient.
/// Endpoints and fields were checked against PayPal's Invoicing v2 description (version 2.12.0);
/// see docs/multitenancy/paypal-notes.md. Payloads are never logged: they hold payer names and emails.
/// </summary>
public sealed class PayPalInvoiceProvider : IInvoiceProvider
{
    private const string Invoices = "v2/invoicing/invoices";

    private readonly PayPalOptions _options;
    private readonly PayPalRest _rest;
    private readonly ILogger<PayPalInvoiceProvider> _log;

    public PayPalInvoiceProvider(IOptions<PayPalOptions> options, IHttpClientFactory httpFactory, PayPalAccessTokens tokens, ILogger<PayPalInvoiceProvider> log)
    {
        _options = options.Value;
        _rest = new PayPalRest(httpFactory.CreateClient(PayPalPaymentProvider.HttpClientName), tokens);
        _log = log;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<string> CreateDraftAsync(NewPayPalInvoice invoice, CancellationToken ct = default)
    {
        RequireConfigured();
        var body = new JsonObject
        {
            ["detail"] = new JsonObject
            {
                ["invoice_number"] = invoice.Number,
                ["invoice_date"] = Date(invoice.InvoiceDate),
                ["currency_code"] = invoice.Currency,
                ["payment_term"] = new JsonObject { ["term_type"] = "DUE_ON_DATE_SPECIFIED", ["due_date"] = Date(invoice.DueDate) },
            },
            ["primary_recipients"] = new JsonArray(new JsonObject { ["billing_info"] = Recipient(invoice) }),
            ["items"] = new JsonArray(invoice.Lines.Select(l => (JsonNode)new JsonObject
            {
                ["name"] = Cut(l.Name, InvoiceLine.MaxDescriptionLength),
                ["quantity"] = l.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
                ["unit_amount"] = Money(l.UnitAmount, invoice.Currency),
                ["unit_of_measure"] = "QUANTITY",
            }).ToArray()),
            ["configuration"] = new JsonObject { ["allow_tip"] = false, ["partial_payment"] = new JsonObject { ["allow_partial_payment"] = false } },
        };
        if (!string.IsNullOrWhiteSpace(invoice.Note)) body["detail"]!["note"] = Cut(invoice.Note, 4000);
        if (!string.IsNullOrWhiteSpace(invoice.Reference)) body["detail"]!["reference"] = Cut(invoice.Reference, 120);

        // Not retried by the pipeline: the create has no PayPal-Request-Id. Our invoice number makes it
        // safe to call again: PayPal refuses a second invoice with the same number, and we look it up.
        using var response = await _rest.SendAsync(HttpMethod.Post, Invoices, body, null, ct, retry: false, prefer: "return=representation").ConfigureAwait(false);
        try
        {
            var created = await PayPalRest.ReadAsync(response, "create an invoice", ct).ConfigureAwait(false);
            return IdOf(created) ?? throw new PaymentProviderException("PayPal created an invoice without an id.", (int)response.StatusCode, PayPalRest.DebugId(response));
        }
        catch (PaymentProviderException e) when (e.Issue == "DUPLICATE_INVOICE_NUMBER")
        {
            var existing = await FindByNumberAsync(invoice.Number, ct).ConfigureAwait(false);
            if (existing != null) return existing;
            throw;
        }
    }

    public async Task<string> SendAsync(string invoiceId, CancellationToken ct = default)
    {
        RequireConfigured();
        var body = new JsonObject { ["send_to_invoicer"] = true, ["send_to_recipient"] = true };
        using var response = await _rest.SendAsync(HttpMethod.Post, $"{Invoices}/{Id(invoiceId)}/send", body, null, ct, retry: false).ConfigureAwait(false);
        string? href = null;
        try
        {
            href = (await PayPalRest.ReadAsync(response, "send an invoice", ct).ConfigureAwait(false))["href"]?.GetValue<string>();
        }
        catch (PaymentProviderException e) when (e.Issue is "INVOICE_ALREADY_SENT" or "CANT_SEND_INVOICE_IN_CURRENT_STATE")
        {
            // Sent before (e.g. our first attempt's answer was lost): the payer page is read below.
        }
        if (IsPayerPage(href)) return href!;
        var info = await GetAsync(invoiceId, ct).ConfigureAwait(false);
        return info?.PayerUrl ?? throw new PaymentProviderException("PayPal sent the invoice but gave no payer page.");
    }

    public async Task<PayPalInvoiceInfo?> GetAsync(string invoiceId, CancellationToken ct = default)
    {
        RequireConfigured();
        using var response = await _rest.SendAsync(HttpMethod.Get, $"{Invoices}/{Id(invoiceId)}", null, null, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return Map(await PayPalRest.ReadAsync(response, "read an invoice", ct).ConfigureAwait(false));
    }

    public async Task CancelAsync(string invoiceId, string note, CancellationToken ct = default)
    {
        RequireConfigured();
        var info = await GetAsync(invoiceId, ct).ConfigureAwait(false) ?? throw new PaymentProviderException("PayPal does not know this invoice.", 404);
        if (info.State == PayPalInvoiceState.Cancelled) return;
        if (info.State == PayPalInvoiceState.Draft)
        {
            using var deleted = await _rest.SendAsync(HttpMethod.Delete, $"{Invoices}/{Id(invoiceId)}", null, null, ct).ConfigureAwait(false);
            if (deleted.StatusCode != HttpStatusCode.NotFound) await PayPalRest.ReadAsync(deleted, "delete a draft invoice", ct).ConfigureAwait(false);
            return;
        }
        var body = new JsonObject { ["note"] = Cut(note, 500), ["send_to_invoicer"] = true, ["send_to_recipient"] = true };
        using var response = await _rest.SendAsync(HttpMethod.Post, $"{Invoices}/{Id(invoiceId)}/cancel", body, null, ct).ConfigureAwait(false);
        await PayPalRest.ReadAsync(response, "cancel an invoice", ct).ConfigureAwait(false);
    }

    public async Task<string> RecordExternalPaymentAsync(string invoiceId, DateOnly date, OfflinePaymentMethod method, decimal amount, string currency, string? note, CancellationToken ct = default)
    {
        RequireConfigured();
        var body = new JsonObject { ["method"] = MethodCode(method), ["payment_date"] = Date(date), ["amount"] = Money(amount, currency) };
        if (!string.IsNullOrWhiteSpace(note)) body["note"] = Cut(note, 2000);
        // Never retried: a repeat would record the payment twice at PayPal.
        using var response = await _rest.SendAsync(HttpMethod.Post, $"{Invoices}/{Id(invoiceId)}/payments", body, null, ct, retry: false).ConfigureAwait(false);
        return (await PayPalRest.ReadAsync(response, "record a payment", ct).ConfigureAwait(false))["payment_id"]?.GetValue<string>()
            ?? throw new PaymentProviderException("PayPal recorded a payment without an id.", (int)response.StatusCode, PayPalRest.DebugId(response));
    }

    public async Task<string> RecordExternalRefundAsync(string invoiceId, DateOnly date, OfflinePaymentMethod method, decimal amount, string currency, CancellationToken ct = default)
    {
        RequireConfigured();
        var body = new JsonObject { ["method"] = MethodCode(method), ["refund_date"] = Date(date), ["amount"] = Money(amount, currency) };
        using var response = await _rest.SendAsync(HttpMethod.Post, $"{Invoices}/{Id(invoiceId)}/refunds", body, null, ct, retry: false).ConfigureAwait(false);
        return (await PayPalRest.ReadAsync(response, "record a refund", ct).ConfigureAwait(false))["refund_id"]?.GetValue<string>()
            ?? throw new PaymentProviderException("PayPal recorded a refund without an id.", (int)response.StatusCode, PayPalRest.DebugId(response));
    }

    public async Task<ProviderRefund> RefundAsync(RefundTarget target, string transactionId, decimal amount, string currency, string requestId, string? note, CancellationToken ct = default)
    {
        RequireConfigured();
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("A request id makes the refund safe to retry.", nameof(requestId));
        if (amount <= 0 || decimal.Round(amount, 2) != amount) throw new ArgumentOutOfRangeException(nameof(amount));

        // Subscription payments are PayPal sales (v1); invoice payments are captures (v2). Both accept
        // PayPal-Request-Id, so the pipeline may retry them: PayPal refunds once.
        JsonNode body;
        string path;
        if (target == RefundTarget.SubscriptionSale)
        {
            path = $"v1/payments/sale/{Id(transactionId)}/refund";
            body = new JsonObject { ["amount"] = new JsonObject { ["total"] = Format(amount), ["currency"] = currency } };
            if (!string.IsNullOrWhiteSpace(note)) body["description"] = Cut(note, 255);
        }
        else
        {
            path = $"v2/payments/captures/{Id(transactionId)}/refund";
            body = new JsonObject { ["amount"] = Money(amount, currency) };
            if (!string.IsNullOrWhiteSpace(note)) body["note_to_payer"] = Cut(note, 255);
        }
        using var response = await _rest.SendAsync(HttpMethod.Post, path, body, requestId, ct, prefer: "return=representation").ConfigureAwait(false);
        var refund = await PayPalRest.ReadAsync(response, "refund a payment", ct).ConfigureAwait(false);
        var id = refund["id"]?.GetValue<string>() ?? throw new PaymentProviderException("PayPal refunded without a refund id.", (int)response.StatusCode, PayPalRest.DebugId(response));
        return new ProviderRefund(id, (refund["status"] ?? refund["state"])?.GetValue<string>()?.ToUpperInvariant());
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        RequireConfigured();
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(31)) throw new ArgumentOutOfRangeException(nameof(toUtc), "PayPal searches at most 31 days at once.");
        var start = Uri.EscapeDataString(fromUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var end = Uri.EscapeDataString(toUtc.AddSeconds(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var list = new List<PayPalTransaction>();
        for (var page = 1; ; page++)
        {
            using var response = await _rest.SendAsync(HttpMethod.Get,
                $"v1/reporting/transactions?start_date={start}&end_date={end}&fields=transaction_info&balance_affecting_records_only=Y&page_size=500&page={page}",
                null, null, ct).ConfigureAwait(false);
            var found = await PayPalRest.ReadAsync(response, "search transactions", ct).ConfigureAwait(false);
            foreach (var detail in found["transaction_details"] as JsonArray ?? new JsonArray())
            {
                var info = detail?["transaction_info"];
                var id = info?["transaction_id"]?.GetValue<string>();
                var amount = Amount(info?["transaction_amount"]?["value"]?.GetValue<string>());
                if (id == null || amount == null) continue;
                var when = DateTimeOffset.TryParse(info!["transaction_initiation_date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.UtcDateTime : fromUtc;
                list.Add(new PayPalTransaction(id, info["transaction_event_code"]?.GetValue<string>(), when, amount.Value,
                    info["transaction_amount"]?["currency_code"]?.GetValue<string>() ?? "", info["transaction_status"]?.GetValue<string>(), info["invoice_id"]?.GetValue<string>()));
            }
            var pages = found["total_pages"]?.GetValue<int>() ?? 1;
            if (page >= pages || page >= 100) break;
        }
        return list;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<string?> FindByNumberAsync(string number, CancellationToken ct)
    {
        using var response = await _rest.SendAsync(HttpMethod.Post, "v2/invoicing/search-invoices?page=1&page_size=5", new JsonObject { ["invoice_number"] = number }, null, ct).ConfigureAwait(false);
        var found = await PayPalRest.ReadAsync(response, "search invoices", ct).ConfigureAwait(false);
        var match = (found["items"] as JsonArray)?.FirstOrDefault(i => i?["detail"]?["invoice_number"]?.GetValue<string>() == number);
        if (match != null) _log.LogInformation("PayPal already had invoice {Number}: using it.", number);
        return match == null ? null : IdOf(match);
    }

    /// <summary>The invoice id: from the representation, or the last segment of the minimal answer's link.</summary>
    private static string? IdOf(JsonNode node)
    {
        var id = node["id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(id)) return id;
        var href = node["href"]?.GetValue<string>();
        return Uri.TryCreate(href, UriKind.Absolute, out var uri) ? uri.Segments.LastOrDefault()?.Trim('/') : null;
    }

    private static JsonObject Recipient(NewPayPalInvoice invoice)
    {
        var billing = new JsonObject { ["email_address"] = invoice.RecipientEmail };
        if (!string.IsNullOrWhiteSpace(invoice.RecipientBusinessName)) billing["business_name"] = Cut(invoice.RecipientBusinessName, 300);
        if (!string.IsNullOrWhiteSpace(invoice.RecipientGivenName) || !string.IsNullOrWhiteSpace(invoice.RecipientSurname))
        {
            var name = new JsonObject();
            if (!string.IsNullOrWhiteSpace(invoice.RecipientGivenName)) name["given_name"] = Cut(invoice.RecipientGivenName, 140);
            if (!string.IsNullOrWhiteSpace(invoice.RecipientSurname)) name["surname"] = Cut(invoice.RecipientSurname, 140);
            billing["name"] = name;
        }
        return billing;
    }

    private static PayPalInvoiceInfo Map(JsonNode invoice)
    {
        var raw = invoice["status"]?.GetValue<string>();
        var currency = invoice["amount"]?["currency_code"]?.GetValue<string>() ?? invoice["detail"]?["currency_code"]?.GetValue<string>() ?? "";
        var payments = (invoice["payments"]?["transactions"] as JsonArray ?? new JsonArray()).Where(t => t != null).Select(t => new PayPalInvoicePayment(
            t!["payment_id"]?.GetValue<string>(),
            t["type"]?.GetValue<string>() == "EXTERNAL",
            DateOf(t["payment_date"]?.GetValue<string>()),
            Amount(t["amount"]?["value"]?.GetValue<string>()) ?? 0,
            t["amount"]?["currency_code"]?.GetValue<string>() ?? currency,
            t["method"]?.GetValue<string>())).ToList();
        var refunds = (invoice["refunds"]?["transactions"] as JsonArray ?? new JsonArray()).Where(t => t != null).Select(t => new PayPalInvoiceRefund(
            t!["refund_id"]?.GetValue<string>(),
            t["type"]?.GetValue<string>() == "EXTERNAL",
            DateOf(t["refund_date"]?.GetValue<string>()),
            Amount(t["amount"]?["value"]?.GetValue<string>()) ?? 0,
            t["amount"]?["currency_code"]?.GetValue<string>() ?? currency)).ToList();
        return new PayPalInvoiceInfo(
            invoice["id"]?.GetValue<string>() ?? "",
            invoice["detail"]?["invoice_number"]?.GetValue<string>(),
            State(raw),
            raw,
            Amount(invoice["amount"]?["value"]?.GetValue<string>()) ?? 0,
            currency,
            Amount(invoice["payments"]?["paid_amount"]?["value"]?.GetValue<string>()) ?? payments.Sum(p => p.Amount),
            Amount(invoice["refunds"]?["refund_amount"]?["value"]?.GetValue<string>()) ?? refunds.Sum(r => r.Amount),
            DateOf(invoice["detail"]?["payment_term"]?["due_date"]?.GetValue<string>()),
            invoice["detail"]?["metadata"]?["recipient_view_url"]?.GetValue<string>(),
            payments,
            refunds);
    }

    /// <summary>PayPal's 16 invoice statuses, grouped.</summary>
    public static PayPalInvoiceState State(string? status) => status?.ToUpperInvariant() switch
    {
        "DRAFT" => PayPalInvoiceState.Draft,
        "SCHEDULED" => PayPalInvoiceState.Scheduled,
        "SENT" or "UNPAID" or "SHARED" => PayPalInvoiceState.Sent,
        "PAYMENT_PENDING" => PayPalInvoiceState.PaymentPending,
        "PARTIALLY_PAID" => PayPalInvoiceState.PartiallyPaid,
        "PAID" or "MARKED_AS_PAID" or "PAID_EXTERNAL" => PayPalInvoiceState.Paid,
        "PARTIALLY_REFUNDED" => PayPalInvoiceState.PartiallyRefunded,
        "REFUNDED" or "MARKED_AS_REFUNDED" or "REFUNDED_EXTERNAL" => PayPalInvoiceState.Refunded,
        "CANCELLED" or "AUTO_CANCELLED" => PayPalInvoiceState.Cancelled,
        _ => PayPalInvoiceState.Unknown,
    };

    /// <summary>PayPal's payment methods; mobile money has none (OTHER, with the method in our records).</summary>
    public static string MethodCode(OfflinePaymentMethod method) => method switch
    {
        OfflinePaymentMethod.BankTransfer => "BANK_TRANSFER",
        OfflinePaymentMethod.Cash => "CASH",
        OfflinePaymentMethod.Cheque => "CHECK",
        _ => "OTHER",
    };

    private static bool IsPayerPage(string? href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !uri.Host.StartsWith("api", StringComparison.OrdinalIgnoreCase);

    private static JsonObject Money(decimal amount, string currency) => new() { ["currency_code"] = currency, ["value"] = Format(amount) };

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? DateOf(string? value) =>
        value != null && value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? Amount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static string Cut(string value, int max) => value.Length > max ? value[..max] : value;

    /// <summary>Ids go into the path: only PayPal's id characters.</summary>
    private static string Id(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 50 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? id : throw new ArgumentException("Not a PayPal id.", nameof(id));

    private void RequireConfigured()
    {
        if (!IsConfigured) throw new PaymentProviderException("PayPal is not configured (PayPal:ClientId and PayPal:Secret).");
    }
}
