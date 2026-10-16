using System.Net;
using System.Text;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 4 step 4.3: PayPal's invoice and refund messages (P4-1 with the pretend PayPal). The job
/// re-reads the invoice from PayPal; payments and refunds are recorded once by PayPal's ids, so
/// repeats change nothing and partial refunds add up; the daily check corrects a dropped message.
/// </summary>
public class InvoiceWebhookTests : IClassFixture<ProviderFixture>
{
    private const string GoodSignature = "good-signature";
    private readonly ProviderFixture _app;

    public InvoiceWebhookTests(ProviderFixture app)
    {
        _app = app;
        _app.PayPal.Verify = m => m.Headers.TryGetValue("PAYPAL-TRANSMISSION-SIG", out var sig) && sig == GoodSignature;
    }

    private async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static string Event(string type, string resourceType, string resourceJson) =>
        $$"""{"id":"WH-{{Guid.NewGuid():N}}","event_type":"{{type}}","resource_type":"{{resourceType}}","create_time":"2026-10-16T10:00:00Z","resource":{{resourceJson}}}""";

    private static string InvoiceEvent(string type, string invoiceId) => Event(type, "invoices", $$"""{"id":"{{invoiceId}}","status":"WHATEVER"}""");

    private static string CaptureRefundEvent(string captureId, string refundId, decimal amount, string status = "COMPLETED") => Event("PAYMENT.CAPTURE.REFUNDED", "refund",
        $$$"""{"id":"{{{refundId}}}","status":"{{{status}}}","amount":{"value":"{{{amount:0.00}}}","currency_code":"USD"},"links":[{"href":"https://api-m.sandbox.paypal.com/v2/payments/refunds/{{{refundId}}}","rel":"self"},{"href":"https://api-m.sandbox.paypal.com/v2/payments/captures/{{{captureId}}}","rel":"up"}]}""");

    private static string SaleRefundEvent(string saleId, string refundId, decimal amount) => Event("PAYMENT.SALE.REFUNDED", "refund",
        $$$"""{"id":"{{{refundId}}}","sale_id":"{{{saleId}}}","state":"completed","amount":{"total":"{{{amount:0.00}}}","currency":"USD"}}""");

    /// <summary>Through the endpoint, then the job here (no job server in tests).</summary>
    private async Task DeliverAsync(string body)
    {
        using var client = _app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/paypal") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in new[] { ("PAYPAL-AUTH-ALGO", "SHA256withRSA"), ("PAYPAL-CERT-URL", "https://api.sandbox.paypal.com/v1/notifications/certs/CERT"),
                     ("PAYPAL-TRANSMISSION-ID", Guid.NewGuid().ToString()), ("PAYPAL-TRANSMISSION-SIG", GoodSignature), ("PAYPAL-TRANSMISSION-TIME", "2026-10-16T10:00:00Z") })
            request.Headers.Add(name, value);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        var id = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
        await UseAsync(async sp => { await sp.GetRequiredService<BillingEventProcessor>().ProcessAsync(id); return true; });
    }

    /// <summary>A PayPal invoice of 70 USD sent to Alpha (through the console's service).</summary>
    private async Task<(Guid Id, string ExternalId)> SentInvoiceAsync()
    {
        var result = await UseAsync(sp => sp.GetRequiredService<InvoiceService>().CreateAndSendAsync(new NewInvoiceInput(_app.Data.Alpha.Id, InvoiceSource.PayPalInvoice,
            new[] { new InvoiceLineInput("Setup", 1, 50), new InvoiceLineInput("Training", 2, 10) }, DateOnly.FromDateTime(_app.Clock.UtcNow).AddDays(14), null), "provider:test"));
        Assert.True(result.Succeeded, result.Error);
        var invoice = await InvoiceAsync(result.InvoiceId!.Value);
        return (invoice.Id, invoice.ExternalInvoiceId!);
    }

    private async Task<Invoice> InvoiceAsync(Guid id)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    private async Task<Payment> PaymentAsync(string externalId)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Payments.AsNoTracking().SingleAsync(p => p.ExternalId == externalId);
    }

    [Fact]
    public async Task An_Invoice_Paid_At_PayPal_Is_Recorded_Once()
    {
        var (id, external) = await SentInvoiceAsync();
        var capture = _app.Invoicing.PayByPayPal(external);

        await DeliverAsync(InvoiceEvent("INVOICING.INVOICE.PAID", external));
        await DeliverAsync(InvoiceEvent("INVOICING.INVOICE.PAID", external)); // PayPal sends again
        await DeliverAsync(InvoiceEvent("INVOICING.INVOICE.UPDATED", external));

        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.Paid, 70m, 0m), (invoice.Status, invoice.PaidAmount, invoice.Outstanding));
        var payment = await PaymentAsync(capture);
        Assert.Equal((PaymentSource.PayPal, id, 70m), (payment.Source, payment.InvoiceId, payment.Amount));
        await using var db = _app.CreatePlatformDbContext();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.InvoiceId == id));
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Target == capture && a.Action == "payment.received"));
    }

    [Fact]
    public async Task Partial_Refunds_Of_An_Invoice_Payment_Add_Up_And_Repeats_Change_Nothing()
    {
        var (id, external) = await SentInvoiceAsync();
        var capture = _app.Invoicing.PayByPayPal(external);
        await DeliverAsync(InvoiceEvent("INVOICING.INVOICE.PAID", external));

        await DeliverAsync(CaptureRefundEvent(capture, "RF-CAP-1-" + capture, 20m));
        await DeliverAsync(CaptureRefundEvent(capture, "RF-CAP-2-" + capture, 5m));
        await DeliverAsync(CaptureRefundEvent(capture, "RF-CAP-2-" + capture, 5m)); // the same refund again

        var payment = await PaymentAsync(capture);
        Assert.Equal((PaymentStatus.Refunded, 25m), (payment.Status, payment.RefundedAmount));
        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.PartiallyRefunded, 25m), (invoice.Status, invoice.RefundedAmount));
        await using var db = _app.CreatePlatformDbContext();
        Assert.Equal(2, await db.PaymentRefunds.CountAsync(r => r.PaymentId == payment.Id));
    }

    [Fact]
    public async Task Partial_Refunds_Of_A_Subscription_Payment_Add_Up()
    {
        // Phase 2 kept only the last refund's amount as the total; refunds are now rows (step 4.2).
        var sale = $"SALE{Guid.NewGuid():N}"[..17].ToUpperInvariant();
        await using (var db = _app.CreatePlatformDbContext())
        {
            db.Payments.Add(Payment.Record(_app.Data.Alpha.Id, null, PaymentSource.PayPal, sale, 29m, "USD", _app.Clock.UtcNow, "paypal:webhook"));
            await db.SaveChangesAsync();
        }
        await DeliverAsync(SaleRefundEvent(sale, "RS1-" + sale, 10m));
        await DeliverAsync(SaleRefundEvent(sale, "RS2-" + sale, 5m));
        Assert.Equal(15m, (await PaymentAsync(sale)).RefundedAmount);

        // A refund made from the console, then reported by PayPal: one row, not two.
        var payment = await PaymentAsync(sale);
        Assert.True((await UseAsync(sp => sp.GetRequiredService<InvoiceService>().RefundAsync(payment.Id, 4m, "Goodwill", "provider:test", isOwner: true))).Succeeded);
        string consoleRefundId;
        await using (var db = _app.CreatePlatformDbContext())
            consoleRefundId = (await db.PaymentRefunds.Where(r => r.PaymentId == payment.Id).OrderByDescending(r => r.RefundedUtc).FirstAsync()).ExternalRefundId!;
        await DeliverAsync(SaleRefundEvent(sale, consoleRefundId, 4m));
        Assert.Equal(19m, (await PaymentAsync(sale)).RefundedAmount);
        await using var check = _app.CreatePlatformDbContext();
        Assert.Equal(3, await check.PaymentRefunds.CountAsync(r => r.PaymentId == payment.Id));
    }

    [Fact]
    public async Task An_Invoice_Cancelled_At_PayPal_Is_Cancelled_Here()
    {
        var (id, external) = await SentInvoiceAsync();
        _app.Invoicing.Get(external).State = PayPalInvoiceState.Cancelled;
        await DeliverAsync(InvoiceEvent("INVOICING.INVOICE.CANCELLED", external));

        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.Cancelled, "Cancelled at PayPal"), (invoice.Status, invoice.CancelReason));
    }

    [Fact]
    public async Task A_Chargeback_On_An_Invoice_Payment_Reopens_The_Invoice()
    {
        var (id, external) = await SentInvoiceAsync();
        var capture = _app.Invoicing.PayByPayPal(external);
        await DeliverAsync(InvoiceEvent("INVOICING.INVOICE.PAID", external));
        await DeliverAsync(Event("PAYMENT.CAPTURE.REVERSED", "capture", $$$"""{"id":"{{{capture}}}","status":"REVERSED","amount":{"value":"70.00","currency_code":"USD"}}"""));

        Assert.Equal(PaymentStatus.Reversed, (await PaymentAsync(capture)).Status);
        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.Sent, 70m), (invoice.Status, invoice.Outstanding));
    }

    [Fact]
    public async Task A_Dropped_Message_Is_Corrected_By_The_Daily_Check()
    {
        var (id, external) = await SentInvoiceAsync();
        var capture = _app.Invoicing.PayByPayPal(external); // PayPal's message never arrives

        await UseAsync(sp => sp.GetRequiredService<BillingReconciliation>().DailyAsync());

        Assert.Equal(InvoiceStatus.Paid, (await InvoiceAsync(id)).Status);
        Assert.Equal(70m, (await PaymentAsync(capture)).Amount);
        await using var db = _app.CreatePlatformDbContext();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Target == capture && a.Action == "payment.received" && a.Actor == "job:reconciliation"));
    }
}
