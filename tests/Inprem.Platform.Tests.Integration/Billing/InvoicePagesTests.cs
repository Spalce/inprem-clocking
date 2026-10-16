using System.Net;
using AngleSharp.Html.Dom;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 4 step 4.2 (P4-2): invoices from the console (new → review → create and send; PayPal or
/// offline), resend, cancel, offline payments and refunds, with their role checks, the fresh
/// two-factor code for refunds, the Billing refund limit (D28) and one audit entry per action.
/// </summary>
public class InvoicePagesTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public InvoicePagesTests(ProviderFixture app) => _app = app;

    private async Task<(HttpClient Client, string Key)> SignedInAsync(ProviderRole role)
    {
        var email = $"inv-{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..22] + "@console.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, role);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return (client, key);
    }

    /// <summary>Posts the page's form to one of its handlers (the buttons choose the handler).</summary>
    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string page, string handler, params (string Name, string Value)[] fields)
    {
        var document = await GetDocumentAsync(client, page);
        var token = document.QuerySelectorAll("form[method=post] input[name=__RequestVerificationToken]").First().GetAttribute("value")!;
        var values = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token) };
        values.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
        var path = page.Split('?')[0];
        return await client.PostAsync($"{path}?handler={handler}", new FormUrlEncodedContent(values));
    }

    private static (string, string)[] InvoiceFields(int tenantId, InvoiceSource source, DateOnly due) => new[]
    {
        ("TenantId", tenantId.ToString()), ("Source", source.ToString()), ("DueDate", due.ToString("yyyy-MM-dd")), ("Notes", "Thank you"),
        ("Lines[0].Description", "Setup and training"), ("Lines[0].Quantity", "1"), ("Lines[0].UnitAmount", "50"),
        ("Lines[1].Description", "Extra kiosk (months)"), ("Lines[1].Quantity", "2"), ("Lines[1].UnitAmount", "10"),
        ("Lines[2].Description", ""), ("Lines[2].Quantity", ""), ("Lines[2].UnitAmount", ""),
    };

    private DateOnly Due => DateOnly.FromDateTime(_app.Clock.UtcNow).AddDays(14);

    private async Task<Guid> CreateInvoiceAsync(HttpClient client, InvoiceSource source)
    {
        var created = await SubmitAsync(client, $"/Provider/Billing/Invoices/New?tenantId={_app.Data.Alpha.Id}", "Create", InvoiceFields(_app.Data.Alpha.Id, source, Due));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        return Guid.Parse(Location(created).Split('/').Last());
    }

    private async Task<Invoice> InvoiceAsync(Guid id)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    private async Task<List<string>> AuditAsync(string target)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.Where(a => a.Target == target).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
    }

    [Fact]
    public async Task A_PayPal_Invoice_Is_Reviewed_Then_Created_And_Sent_By_PayPal()
    {
        var (owner, _) = await SignedInAsync(ProviderRole.Owner);
        var page = $"/Provider/Billing/Invoices/New?tenantId={_app.Data.Alpha.Id}";
        await using (var db = _app.CreatePlatformDbContext())
        {
            var before = await db.Invoices.CountAsync();

            // Review shows the total and saves nothing.
            var review = await SubmitAsync(owner, page, "Review", InvoiceFields(_app.Data.Alpha.Id, InvoiceSource.PayPalInvoice, Due));
            var html = await BodyAsync(review);
            Assert.Contains("data-testid=\"review\"", html);
            Assert.Contains("70.00 USD", html);
            Assert.Equal(before, await db.Invoices.CountAsync());
        }

        var id = await CreateInvoiceAsync(owner, InvoiceSource.PayPalInvoice);
        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.Sent, 70m, InvoiceSource.PayPalInvoice), (invoice.Status, invoice.Total, invoice.Source));
        Assert.StartsWith("https://www.sandbox.paypal.com/invoice/p/", invoice.PaymentUrl);
        var atPayPal = _app.Invoicing.Get(invoice.ExternalInvoiceId!);
        Assert.Equal((invoice.Number, PayPalInvoiceState.Sent, 2), (atPayPal.Request.Number, atPayPal.State, atPayPal.Request.Lines.Count));
        Assert.Equal(_app.Data.Alpha.Owner!.Email, atPayPal.Request.RecipientEmail);
        Assert.Equal(new[] { "invoice.created", "invoice.sent" }, await AuditAsync(id.ToString()));

        var details = await GetDocumentAsync(owner, $"/Provider/Billing/Invoices/Details/{id}");
        Assert.NotNull(details.QuerySelector("[data-testid=paypal-link]"));
        Assert.Contains(invoice.Number, (await GetDocumentAsync(owner, "/Provider/Billing/Invoices")).QuerySelector("[data-testid=invoices]")!.TextContent);
        Assert.Contains(invoice.Number, (await GetDocumentAsync(owner, $"/Provider/Tenants/Details/{_app.Data.Alpha.Id}?tab=invoices")).QuerySelector("[data-testid=invoices]")!.TextContent);

        // An offline payment on a PayPal invoice is recorded at PayPal too.
        var recorded = await PostAsync(owner, $"/Provider/Billing/Invoices/RecordPayment/{id}",
            ("PaidOn", DateOnly.FromDateTime(_app.Clock.UtcNow).ToString("yyyy-MM-dd")), ("Method", "MobileMoney"), ("Reference", $"MOMO-{Guid.NewGuid():N}"[..16]), ("Amount", "70"));
        Assert.Equal(HttpStatusCode.Redirect, recorded.StatusCode);
        Assert.Equal(InvoiceStatus.Paid, (await InvoiceAsync(id)).Status);
        Assert.Contains(atPayPal.Payments, p => p.External && p.Amount == 70m && p.Method == "OTHER");
    }

    [Fact]
    public async Task When_PayPal_Is_Unreachable_The_Invoice_Stays_A_Draft_And_Is_Sent_Later()
    {
        var (billing, _) = await SignedInAsync(ProviderRole.Billing);
        _app.Invoicing.FailNext = true;
        var id = await CreateInvoiceAsync(billing, InvoiceSource.PayPalInvoice);

        Assert.Equal(InvoiceStatus.Draft, (await InvoiceAsync(id)).Status);
        Assert.Equal(new[] { "invoice.created", "invoice.send-failed" }, await AuditAsync(id.ToString()));
        var details = await GetDocumentAsync(billing, $"/Provider/Billing/Invoices/Details/{id}");
        Assert.Contains("PayPal is not answering", details.QuerySelector("[data-testid=error]")!.TextContent);

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(billing, $"/Provider/Billing/Invoices/Details/{id}", "handler=Resend")).StatusCode);
        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
        Assert.Equal(new[] { "invoice.created", "invoice.send-failed", "invoice.sent" }, await AuditAsync(id.ToString()));

        // Sending again now reminds the organization.
        await PostFormAsync(billing, $"/Provider/Billing/Invoices/Details/{id}", "handler=Resend");
        Assert.Equal("invoice.resent", (await AuditAsync(id.ToString())).Last());
    }

    [Fact]
    public async Task An_Offline_Invoice_Is_Emailed_Paid_In_Parts_And_References_Are_Not_Recorded_Twice()
    {
        var (owner, _) = await SignedInAsync(ProviderRole.Owner);
        var id = await CreateInvoiceAsync(owner, InvoiceSource.Manual);
        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.Sent, null), (invoice.Status, invoice.ExternalInvoiceId));
        var mail = _app.Emails.To(_app.Data.Alpha.Owner!.Email).Last(e => e.Subject.Contains(invoice.Number));
        Assert.Contains("70.00 USD", mail.HtmlBody);

        var page = $"/Provider/Billing/Invoices/RecordPayment/{id}";
        var today = DateOnly.FromDateTime(_app.Clock.UtcNow).ToString("yyyy-MM-dd");
        var reference = $"GCB-{Guid.NewGuid():N}"[..14];
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(owner, page, ("PaidOn", today), ("Method", "BankTransfer"), ("Reference", reference), ("Amount", "30"))).StatusCode);
        Assert.Equal((InvoiceStatus.PartiallyPaid, 40m), ((await InvoiceAsync(id)).Status, (await InvoiceAsync(id)).Outstanding));

        Assert.Contains("already recorded", await BodyAsync(await PostAsync(owner, page, ("PaidOn", today), ("Method", "BankTransfer"), ("Reference", reference), ("Amount", "10"))));
        Assert.Contains("more than is still to pay", await BodyAsync(await PostAsync(owner, page, ("PaidOn", today), ("Method", "Cash"), ("Reference", reference + "-2"), ("Amount", "41"))));
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(owner, page, ("PaidOn", today), ("Method", "Cash"), ("Reference", reference + "-3"), ("Amount", "40"))).StatusCode);
        Assert.Equal(InvoiceStatus.Paid, (await InvoiceAsync(id)).Status);

        // An invoice with payments cannot be cancelled.
        Assert.Contains("cannot be cancelled", await BodyAsync(await PostAsync(owner, $"/Provider/Billing/Invoices/Cancel/{id}", ("Reason", "mistake"))));

        await using var db = _app.CreatePlatformDbContext();
        var payments = await db.Payments.Where(p => p.InvoiceId == id).OrderBy(p => p.Amount).ToListAsync();
        Assert.Equal(new[] { (30m, OfflinePaymentMethod.BankTransfer), (40m, OfflinePaymentMethod.Cash) }, payments.Select(p => (p.Amount, p.Method!.Value)));
        Assert.Equal(new[] { "invoice.created", "invoice.sent", "payment.recorded-offline", "payment.recorded-offline" }, await AuditAsync(id.ToString()));
    }

    [Fact]
    public async Task An_Unpaid_Invoice_Is_Cancelled_At_PayPal_Too()
    {
        var (owner, _) = await SignedInAsync(ProviderRole.Owner);
        var id = await CreateInvoiceAsync(owner, InvoiceSource.PayPalInvoice);
        Assert.Contains("Give a reason", await BodyAsync(await PostAsync(owner, $"/Provider/Billing/Invoices/Cancel/{id}", ("Reason", " "))));
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(owner, $"/Provider/Billing/Invoices/Cancel/{id}", ("Reason", "Sent by mistake"))).StatusCode);

        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.Cancelled, "Sent by mistake"), (invoice.Status, invoice.CancelReason));
        Assert.Equal(PayPalInvoiceState.Cancelled, _app.Invoicing.Get(invoice.ExternalInvoiceId!).State);
        Assert.Equal("invoice.cancelled", (await AuditAsync(id.ToString())).Last());
    }

    private async Task<Guid> SubscriptionPaymentAsync(decimal amount)
    {
        await using var db = _app.CreatePlatformDbContext();
        var payment = Payment.Record(_app.Data.Alpha.Id, null, PaymentSource.PayPal, $"SALE{Guid.NewGuid():N}"[..17].ToUpperInvariant(), amount, "USD", _app.Clock.UtcNow, "paypal:webhook");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment.Id;
    }

    [Fact]
    public async Task An_Owner_Refunds_Through_PayPal_With_A_Fresh_Code_And_Partial_Refunds_Add_Up()
    {
        var (owner, key) = await SignedInAsync(ProviderRole.Owner);
        var paymentId = await SubscriptionPaymentAsync(29m);
        var page = $"/Provider/Billing/Payments/Refund/{paymentId}";

        _app.Clock.Advance(TimeSpan.FromMinutes(6)); // the sign-in's code is no longer recent
        var asked = await PostAsync(owner, page, ("Amount", "10"), ("Reason", "Duplicate charge"));
        Assert.Contains("/Provider/Reauthenticate", Location(asked));
        await PostAsync(owner, Location(asked), ("Code", Totp.Code(key)));

        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(owner, page, ("Amount", "10"), ("Reason", "Duplicate charge"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(owner, page, ("Amount", "5"), ("Reason", "Goodwill"))).StatusCode);
        Assert.Contains("is left to refund", await BodyAsync(await PostAsync(owner, page, ("Amount", "15"), ("Reason", "Too much"))));

        await using var db = _app.CreatePlatformDbContext();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        Assert.Equal((PaymentStatus.Refunded, 15m), (payment.Status, payment.RefundedAmount)); // 10 + 5, not the last one alone
        Assert.Equal(2, await db.PaymentRefunds.CountAsync(r => r.PaymentId == paymentId));
        Assert.Equal(2, _app.Invoicing.MoneyRefunds.Count(r => r.TransactionId == payment.ExternalId && r.Target == RefundTarget.SubscriptionSale));
        Assert.Equal(new[] { "payment.refunded", "payment.refunded" }, await AuditAsync(paymentId.ToString()));
    }

    [Fact]
    public async Task Billing_Users_Refund_Up_To_The_Limit_And_Owners_Alone_When_It_Is_Zero()
    {
        var (billing, _) = await SignedInAsync(ProviderRole.Billing);
        var paymentId = await SubscriptionPaymentAsync(100m);
        var page = $"/Provider/Billing/Payments/Refund/{paymentId}";

        var tooMuch = await BodyAsync(await PostAsync(billing, page, ("Amount", "60"), ("Reason", "Too much for Billing")));
        Assert.True(tooMuch.Contains("up to 50.00 USD"), new AngleSharp.Html.Parser.HtmlParser().ParseDocument(tooMuch).QuerySelector("main")?.TextContent);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, page, ("Amount", (RefundLimit).ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Reason", "Within the limit"))).StatusCode);

        // With the default limit of 0, only Owners refund (D28).
        using var scope = _app.Services.CreateScope();
        var ownersOnly = ActivatorUtilities.CreateInstance<InvoiceService>(scope.ServiceProvider, new BillingOptions { RefundLimit = 0 });
        var refused = await ownersOnly.RefundAsync(paymentId, 1m, "Billing user", "provider:test", isOwner: false);
        Assert.Equal("Only a Provider Owner can refund.", refused.Error);
        Assert.True((await ownersOnly.RefundAsync(paymentId, 1m, "Owner", "provider:test", isOwner: true)).Succeeded);

        // Support users cannot open the page at all.
        var (support, _) = await SignedInAsync(ProviderRole.Support);
        Assert.EndsWith("/Provider/AccessDenied", Location(await support.GetAsync(page)));
    }

    [Fact]
    public async Task Returning_An_Offline_Payment_Is_Recorded_And_The_Invoice_Follows()
    {
        var (owner, _) = await SignedInAsync(ProviderRole.Owner);
        var id = await CreateInvoiceAsync(owner, InvoiceSource.Manual);
        await PostAsync(owner, $"/Provider/Billing/Invoices/RecordPayment/{id}",
            ("PaidOn", DateOnly.FromDateTime(_app.Clock.UtcNow).ToString("yyyy-MM-dd")), ("Method", "Cash"), ("Reference", $"RCPT-{Guid.NewGuid():N}"[..14]), ("Amount", "70"));
        Guid paymentId;
        await using (var db = _app.CreatePlatformDbContext()) paymentId = (await db.Payments.SingleAsync(p => p.InvoiceId == id)).Id;

        var before = _app.Invoicing.MoneyRefunds.Count;
        var done = await PostAsync(owner, $"/Provider/Billing/Payments/Refund/{paymentId}", ("Amount", "20"), ("Reason", "Paid twice for training"));
        Assert.Equal($"/Provider/Billing/Invoices/Details/{id}", Location(done));
        Assert.Equal(before, _app.Invoicing.MoneyRefunds.Count); // offline: nothing goes to PayPal

        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceStatus.PartiallyRefunded, 70m, 20m), (invoice.Status, invoice.PaidAmount, invoice.RefundedAmount));
        var details = await GetDocumentAsync(owner, $"/Provider/Billing/Invoices/Details/{id}");
        Assert.Contains("Paid twice for training", details.QuerySelector("[data-testid=refunds]")!.TextContent);
    }
}
