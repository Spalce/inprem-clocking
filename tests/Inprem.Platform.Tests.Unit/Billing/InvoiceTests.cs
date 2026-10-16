using Inprem.Platform.Domain.Billing;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Billing;

/// <summary>Phase 4 step 4.1: invoice rules (numbers, lines, totals, sending, amounts and status, cancelling).</summary>
public class InvoiceTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 16);
    private const string PayerPage = "https://www.sandbox.paypal.com/invoice/p/#INV2-ABCD";

    private static Invoice New(InvoiceSource source = InvoiceSource.PayPalInvoice, params (string, decimal, decimal)[] lines) =>
        Invoice.Create(7, source == InvoiceSource.Subscription ? Guid.NewGuid() : null, source, "INV-100001", "usd",
            lines.Length == 0 ? new[] { ("Setup", 1m, 50m), ("Training (hours)", 2.5m, 20m) } : lines,
            Today, Today.AddDays(30),
            source == InvoiceSource.Subscription ? new DateOnly(2026, 11, 1) : null, source == InvoiceSource.Subscription ? new DateOnly(2026, 12, 1) : null,
            "  Thank you  ", "owner@inprem.test", Now);

    private static Invoice Sent()
    {
        var invoice = New();
        invoice.CreatedAtPayPal("INV2-ABCD");
        invoice.MarkSent(PayerPage, Now);
        return invoice;
    }

    [Fact]
    public void Numbers_Are_Sequential_And_Readable()
    {
        Assert.Equal("INV-100001", Invoice.FormatNumber(100001));
        Assert.Equal("INV-000042", Invoice.FormatNumber(42));
        Assert.Throws<ArgumentOutOfRangeException>(() => Invoice.FormatNumber(0));
        Assert.Throws<ArgumentException>(() => Invoice.Create(7, null, InvoiceSource.Manual, "100001", "USD", new[] { ("x", 1m, 1m) }, Today, Today, null, null, null, "o", Now));
    }

    [Fact]
    public void A_New_Invoice_Is_A_Draft_With_Its_Lines_And_Total()
    {
        var invoice = New();
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal(100m, invoice.Total); // 50 + 2.5 x 20
        Assert.Equal(new[] { 1, 2 }, invoice.Lines.Select(l => l.Position));
        Assert.Equal("Thank you", invoice.Notes);
        Assert.Equal(0m, invoice.Outstanding); // nothing to pay before it is sent
    }

    [Fact]
    public void Lines_Amounts_Dates_And_Periods_Are_Checked()
    {
        Assert.Throws<ArgumentException>(() => New(InvoiceSource.PayPalInvoice, ("", 1m, 10m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => New(InvoiceSource.PayPalInvoice, ("x", 0m, 10m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => New(InvoiceSource.PayPalInvoice, ("x", 1m, 10.001m)));
        Assert.Throws<ArgumentException>(() => New(InvoiceSource.PayPalInvoice, ("free", 1m, 0m))); // total must be > 0
        Assert.Throws<ArgumentException>(() => Invoice.Create(7, null, InvoiceSource.Manual, "INV-100002", "USD", Array.Empty<(string, decimal, decimal)>(), Today, Today, null, null, null, "o", Now));
        Assert.Throws<ArgumentException>(() => Invoice.Create(7, null, InvoiceSource.Manual, "INV-100002", "USD", new[] { ("x", 1m, 1m) }, Today, Today.AddDays(-1), null, null, null, "o", Now));
        Assert.Throws<ArgumentException>(() => Invoice.Create(7, null, InvoiceSource.Manual, "INV-100002", "USD", new[] { ("x", 1m, 1m) }, Today, Today, Today, null, null, "o", Now));
        // A subscription invoice needs its subscription and period.
        Assert.Throws<ArgumentException>(() => Invoice.Create(7, null, InvoiceSource.Subscription, "INV-100002", "USD", new[] { ("x", 1m, 1m) }, Today, Today, null, null, null, "o", Now));
        Assert.NotNull(New(InvoiceSource.Subscription).PeriodStart);
    }

    [Fact]
    public void A_PayPal_Invoice_Is_Created_At_PayPal_Then_Sent_With_Its_Payer_Page()
    {
        var invoice = New();
        Assert.Throws<InvalidOperationException>(() => invoice.MarkSent(PayerPage, Now)); // not at PayPal yet
        Assert.True(invoice.CreatedAtPayPal("INV2-ABCD"));
        Assert.False(invoice.CreatedAtPayPal("INV2-ABCD")); // a retry: same id, nothing changes
        Assert.Throws<InvalidOperationException>(() => invoice.CreatedAtPayPal("INV2-OTHER"));
        Assert.Throws<ArgumentException>(() => invoice.MarkSent("http://www.paypal.com/x", Now));

        Assert.True(invoice.MarkSent(PayerPage, Now));
        Assert.False(invoice.MarkSent(PayerPage, Now));
        Assert.Equal((InvoiceStatus.Sent, PayerPage, 100m), (invoice.Status, invoice.PaymentUrl, invoice.Outstanding));
    }

    [Fact]
    public void A_Manual_Invoice_Is_Issued_Without_PayPal()
    {
        var invoice = New(InvoiceSource.Manual);
        Assert.Throws<InvalidOperationException>(() => invoice.CreatedAtPayPal("INV2-ABCD"));
        Assert.True(invoice.MarkSent(null, Now));
        Assert.Null(invoice.PaymentUrl);
    }

    [Fact]
    public void The_Status_Follows_What_Was_Paid_And_Refunded()
    {
        var invoice = Sent();
        Assert.Throws<InvalidOperationException>(() => New().ApplyAmounts(10, 0, Now)); // a draft takes no payments

        Assert.True(invoice.ApplyAmounts(40, 0, Now));
        Assert.Equal((InvoiceStatus.PartiallyPaid, 60m), (invoice.Status, invoice.Outstanding));
        Assert.Null(invoice.PaidUtc);

        Assert.True(invoice.ApplyAmounts(100, 0, Now.AddDays(1)));
        Assert.Equal((InvoiceStatus.Paid, 0m, Now.AddDays(1)), (invoice.Status, invoice.Outstanding, invoice.PaidUtc));
        Assert.False(invoice.ApplyAmounts(100, 0, Now.AddDays(2))); // the same again: nothing changes

        Assert.True(invoice.ApplyAmounts(100, 30, Now.AddDays(3)));
        Assert.Equal(InvoiceStatus.PartiallyRefunded, invoice.Status);
        Assert.True(invoice.ApplyAmounts(100, 100, Now.AddDays(4)));
        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);
        Assert.Equal(Now.AddDays(1), invoice.PaidUtc); // when it was paid stays

        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.ApplyAmounts(100, 101, Now));
    }

    [Fact]
    public void Only_An_Invoice_Without_Payments_Is_Cancelled()
    {
        var invoice = Sent();
        Assert.Throws<ArgumentException>(() => invoice.Cancel(" ", Now));
        Assert.True(invoice.Cancel("Sent by mistake", Now));
        Assert.False(invoice.Cancel("again", Now));
        Assert.Equal((InvoiceStatus.Cancelled, "Sent by mistake", 0m), (invoice.Status, invoice.CancelReason, invoice.Outstanding));
        Assert.Throws<InvalidOperationException>(() => invoice.MarkSent(PayerPage, Now));
        Assert.Throws<InvalidOperationException>(() => invoice.ApplyAmounts(10, 0, Now));

        var paid = Sent();
        paid.ApplyAmounts(10, 0, Now);
        Assert.Throws<InvalidOperationException>(() => paid.Cancel("no", Now));
    }

    [Fact]
    public void Payments_Can_Belong_To_An_Invoice_And_Carry_The_Offline_Method()
    {
        var invoiceId = Guid.NewGuid();
        var offline = Payment.Record(7, null, PaymentSource.Offline, "GCB-2026-118", 100m, "USD", Now, "owner@inprem.test", invoiceId, OfflinePaymentMethod.MobileMoney);
        Assert.Equal((invoiceId, OfflinePaymentMethod.MobileMoney), (offline.InvoiceId, offline.Method));
        Assert.Throws<ArgumentException>(() => Payment.Record(7, null, PaymentSource.PayPal, "CAP-1", 100m, "USD", Now, "x", invoiceId, OfflinePaymentMethod.Cash));
    }
}
