using System.Globalization;
using System.Net;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

/// <summary>Configuration section <c>Billing</c> (Phase 4).</summary>
public sealed class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>D28: the most a Provider Billing user may refund at once; 0 (the default) = Owners only.</summary>
    public decimal RefundLimit { get; set; }
}

public sealed record InvoiceLineInput(string Description, decimal Quantity, decimal UnitAmount);

/// <summary>A new invoice from the console: PayPal (sent and paid through PayPal) or Manual (paid offline).</summary>
public sealed record NewInvoiceInput(int TenantId, InvoiceSource Source, IReadOnlyList<InvoiceLineInput> Lines, DateOnly DueDate, string? Notes, string Currency = "USD");

public sealed record OfflinePaymentInput(DateOnly PaidOn, OfflinePaymentMethod Method, string Reference, decimal Amount);

public sealed record InvoiceResult(bool Succeeded, Guid? InvoiceId, string? Error, string? Warning = null)
{
    public static InvoiceResult Fail(string error, Guid? id = null) => new(false, id, error);
}

/// <summary>An invoice in a list, with its organization's name.</summary>
public sealed record InvoiceListItem(Guid Id, string Number, int TenantId, string TenantName, InvoiceSource Source, InvoiceStatus Status,
    decimal Total, decimal Outstanding, string Currency, DateOnly IssueDate, DateOnly DueDate);

public sealed record InvoiceFilter(int? TenantId = null, InvoiceStatus? Status = null, InvoiceSource? Source = null, DateOnly? From = null, DateOnly? To = null, int Page = 1);

public sealed record InvoicePage(IReadOnlyList<InvoiceListItem> Items, int Total, int Page, int Pages);

/// <summary>An invoice with what was paid on it and the refunds of those payments.</summary>
public sealed record InvoiceDetails(Invoice Invoice, string TenantName, IReadOnlyList<Payment> Payments, IReadOnlyList<PaymentRefund> Refunds);

/// <summary>
/// An invoice as its organization sees it (Phase 4 step 4.4: the in-app Billing page and the
/// website's <c>GET /account/invoices</c>). Drafts are never shown. <see cref="Status"/> uses the
/// website API's words: Sent, PartiallyPaid, Overdue (to pay and past its due date), Paid, Refunded, Cancelled.
/// </summary>
public sealed record OrganizationInvoice(Guid Id, string Number, string Status, decimal Total, decimal AmountDue, decimal AmountRefunded, string Currency,
    DateOnly IssueDate, DateOnly DueDate, DateOnly? PeriodStart, DateOnly? PeriodEnd, string? PaymentUrl, bool PaysOffline, string? Notes);

/// <summary>One invoice with its lines and payments, for its organization (the printable invoice and receipt).</summary>
public sealed record OrganizationInvoiceDetails(OrganizationInvoice Invoice, IReadOnlyList<InvoiceLine> Lines, IReadOnlyList<Payment> Payments, string TenantName);

/// <summary>A payment with its refunds and organization (the refund page).</summary>
public sealed record PaymentDetails(Payment Payment, string TenantName, Invoice? Invoice, IReadOnlyList<PaymentRefund> Refunds)
{
    public decimal Refundable => Payment.Status == PaymentStatus.Reversed ? 0 : Payment.Amount - Payment.RefundedAmount;
}

/// <summary>
/// Invoices, offline payments and refunds from the Provider Console (Phase 4 step 4.2). Every
/// action is audited once, with the organization; PayPal is asked first, so a PayPal failure
/// changes nothing here (and is audited as failed). Refunds by a Billing user are capped at
/// <see cref="BillingOptions.RefundLimit"/>; Owners have no cap (D28).
/// </summary>
public sealed class InvoiceService
{
    public const int PageSize = 50;

    private readonly IInvoiceStore _store;
    private readonly IInvoiceProvider _paypal;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly BillingOptions _options;

    private readonly Notifications.BillingAlerts? _alerts;

    public InvoiceService(IInvoiceStore store, IInvoiceProvider paypal, IEmailSender email, IClock clock, BillingOptions options, Notifications.BillingAlerts? alerts = null)
    {
        _store = store;
        _paypal = paypal;
        _email = email;
        _clock = clock;
        _options = options;
        _alerts = alerts;
    }

    /// <summary>Phase 5 step 5.3: a text to the owners once the invoice is out (once per invoice, whatever the resends).</summary>
    private async Task AlertIssuedAsync(Invoice invoice, CancellationToken ct)
    {
        if (_alerts != null && invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled))
            await _alerts.InvoiceIssuedAsync(invoice.Id, invoice.TenantId, invoice.Number, invoice.Total, invoice.Currency, invoice.DueDate, ct).ConfigureAwait(false);
    }

    public decimal RefundLimit => _options.RefundLimit;

    public Task<InvoicePage> ListAsync(InvoiceFilter filter, CancellationToken ct = default) => _store.SearchAsync(filter, PageSize, ct);

    public async Task<InvoiceDetails?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await _store.FindAsync(id, ct).ConfigureAwait(false);
        if (invoice == null) return null;
        var payments = await _store.PaymentsForInvoiceAsync(id, ct).ConfigureAwait(false);
        var refunds = await _store.RefundsForPaymentsAsync(payments.Select(p => p.Id).ToList(), ct).ConfigureAwait(false);
        return new InvoiceDetails(invoice, await _store.TenantNameAsync(invoice.TenantId, ct).ConfigureAwait(false) ?? $"#{invoice.TenantId}", payments, refunds);
    }

    public async Task<PaymentDetails?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        var payment = await _store.FindPaymentAsync(paymentId, ct).ConfigureAwait(false);
        if (payment == null) return null;
        var invoice = payment.InvoiceId == null ? null : await _store.FindAsync(payment.InvoiceId.Value, ct).ConfigureAwait(false);
        return new PaymentDetails(payment, await _store.TenantNameAsync(payment.TenantId, ct).ConfigureAwait(false) ?? $"#{payment.TenantId}", invoice,
            await _store.RefundsForPaymentsAsync(new[] { payment.Id }, ct).ConfigureAwait(false));
    }

    /// <summary>The organization's invoices, newest first, drafts left out (its own only).</summary>
    public async Task<IReadOnlyList<OrganizationInvoice>> ForOrganizationAsync(int tenantId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        return (await _store.ListForTenantAsync(tenantId, 200, ct).ConfigureAwait(false))
            .Where(i => i.Status != InvoiceStatus.Draft).Select(i => View(i, today)).ToList();
    }

    /// <summary>One of the organization's own invoices (null for another organization's, or a draft).</summary>
    public async Task<OrganizationInvoiceDetails?> ForOrganizationAsync(int tenantId, Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _store.FindAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice == null || invoice.TenantId != tenantId || invoice.Status == InvoiceStatus.Draft) return null;
        var payments = await _store.PaymentsForInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        return new OrganizationInvoiceDetails(View(invoice, DateOnly.FromDateTime(_clock.UtcNow)), invoice.Lines, payments,
            await _store.TenantNameAsync(tenantId, ct).ConfigureAwait(false) ?? "");
    }

    private static OrganizationInvoice View(Invoice i, DateOnly today)
    {
        var due = i.Outstanding;
        var status = i.Status switch
        {
            InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid when i.DueDate < today => "Overdue",
            InvoiceStatus.PartiallyRefunded => "Paid", // paid, with part given back (AmountRefunded)
            _ => i.Status.ToString(),
        };
        var payable = due > 0 && i.Status is InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid;
        return new OrganizationInvoice(i.Id, i.Number, status, i.Total, due, i.RefundedAmount, i.Currency, i.IssueDate, i.DueDate, i.PeriodStart, i.PeriodEnd,
            payable && i.Source != InvoiceSource.Manual ? i.PaymentUrl : null, payable && i.Source == InvoiceSource.Manual, i.Notes);
    }

    /// <summary>Checks a new invoice without saving anything (the Review step): the error, or null.</summary>
    public async Task<string?> CheckAsync(NewInvoiceInput input, CancellationToken ct = default)
    {
        if (await _store.TenantNameAsync(input.TenantId, ct).ConfigureAwait(false) == null) return "Choose an organization.";
        if (input.Source is not (InvoiceSource.PayPalInvoice or InvoiceSource.Manual)) return "Choose PayPal or offline payment.";
        if (input.Source == InvoiceSource.PayPalInvoice && !_paypal.IsConfigured) return "PayPal is not configured on this site, so a PayPal invoice cannot be sent. Choose offline payment, or configure PayPal.";
        if (input.Source == InvoiceSource.PayPalInvoice && (await _store.OwnerEmailsAsync(input.TenantId, ct).ConfigureAwait(false)).Count == 0)
            return "The organization has no active owner with an email to send the invoice to.";
        try
        {
            Build(input, "INV-999999", "check");
            return null;
        }
        catch (ArgumentException e)
        {
            return Plain(e);
        }
    }

    /// <summary>Creates the invoice and sends it: through PayPal (PayPal emails it), or by our email for an offline invoice.</summary>
    public async Task<InvoiceResult> CreateAndSendAsync(NewInvoiceInput input, string actor, CancellationToken ct = default)
    {
        var problem = await CheckAsync(input, ct).ConfigureAwait(false);
        if (problem != null) return InvoiceResult.Fail(problem);

        var invoice = Build(input, await _store.NextNumberAsync(ct).ConfigureAwait(false), actor);
        await _store.AddAsync(invoice, ct).ConfigureAwait(false);
        await AuditAsync(actor, "invoice.created", invoice, $"{invoice.Number}: {Money(invoice.Total, invoice.Currency)}, {invoice.Source}, due {invoice.DueDate:yyyy-MM-dd}", ct).ConfigureAwait(false);
        if (await _store.SaveAsync(ct).ConfigureAwait(false) != InvoiceSaveResult.Saved)
            return InvoiceResult.Fail("This subscription period already has an invoice.");

        var sent = await SendAsync(invoice, actor, first: true, ct).ConfigureAwait(false);
        await AlertIssuedAsync(invoice, ct).ConfigureAwait(false);
        return sent ?? new InvoiceResult(true, invoice.Id, null);
    }

    /// <summary>Sends a draft (e.g. after PayPal was unreachable), or reminds the organization of a sent invoice.</summary>
    public async Task<InvoiceResult> ResendAsync(Guid id, string actor, CancellationToken ct = default)
    {
        var invoice = await _store.FindAsync(id, ct).ConfigureAwait(false);
        if (invoice == null) return InvoiceResult.Fail("Invoice not found.");
        if (invoice.Status == InvoiceStatus.Cancelled || invoice.Outstanding == 0 && invoice.Status != InvoiceStatus.Draft)
            return InvoiceResult.Fail("Only an invoice that is still to be paid can be sent again.", id);
        var sent = await SendAsync(invoice, actor, first: invoice.Status == InvoiceStatus.Draft, ct).ConfigureAwait(false);
        await AlertIssuedAsync(invoice, ct).ConfigureAwait(false);
        return sent ?? new InvoiceResult(true, id, null);
    }

    /// <summary>Cancels an invoice nothing was paid on (also at PayPal).</summary>
    public async Task<InvoiceResult> CancelAsync(Guid id, string reason, string actor, CancellationToken ct = default)
    {
        var invoice = await _store.FindAsync(id, ct).ConfigureAwait(false);
        if (invoice == null) return InvoiceResult.Fail("Invoice not found.");
        if (invoice.Status == InvoiceStatus.Cancelled) return new InvoiceResult(true, id, null);
        if (invoice.PaidAmount > 0) return InvoiceResult.Fail("Money was paid on this invoice: it cannot be cancelled. Refund the payment instead.", id);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) return InvoiceResult.Fail("Give a reason (up to 500 characters).", id);

        if (invoice.ExternalInvoiceId != null)
        {
            try
            {
                await _paypal.CancelAsync(invoice.ExternalInvoiceId, reason.Trim(), ct).ConfigureAwait(false);
            }
            catch (PaymentProviderException e)
            {
                return await FailedAsync(actor, "invoice.cancel-failed", invoice, e, "PayPal is not answering right now. Nothing was cancelled; please try again in a few minutes.", ct).ConfigureAwait(false);
            }
        }
        invoice.Cancel(reason, _clock.UtcNow);
        await AuditAsync(actor, "invoice.cancelled", invoice, $"{invoice.Number}: {invoice.CancelReason}", ct).ConfigureAwait(false);
        await _store.SaveAsync(ct).ConfigureAwait(false);
        return new InvoiceResult(true, id, null);
    }

    /// <summary>Records money received offline (bank transfer, cash, cheque, mobile money) on an invoice; also at PayPal for a PayPal invoice.</summary>
    public async Task<InvoiceResult> RecordOfflinePaymentAsync(Guid id, OfflinePaymentInput input, string actor, CancellationToken ct = default)
    {
        var invoice = await _store.FindAsync(id, ct).ConfigureAwait(false);
        if (invoice == null) return InvoiceResult.Fail("Invoice not found.");
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled) return InvoiceResult.Fail("Payments can be recorded only on a sent invoice.", id);
        var reference = (input.Reference ?? "").Trim();
        if (reference.Length is 0 or > 100) return InvoiceResult.Fail("Give the payment's reference (bank or mobile money reference, receipt or cheque number; up to 100 characters).", id);
        if (input.Amount <= 0 || decimal.Round(input.Amount, 2) != input.Amount) return InvoiceResult.Fail("Give an amount above 0 with at most two decimals.", id);
        if (input.Amount > invoice.Outstanding) return InvoiceResult.Fail($"That is more than is still to pay ({Money(invoice.Outstanding, invoice.Currency)}).", id);
        if (input.PaidOn > DateOnly.FromDateTime(_clock.UtcNow).AddDays(1)) return InvoiceResult.Fail("The payment date cannot be in the future.", id);
        if (await _store.OfflineReferenceExistsAsync(reference, ct).ConfigureAwait(false))
            return InvoiceResult.Fail("A payment with this reference is already recorded. Check the invoice's payments before recording it again.", id);

        string? paypalPaymentId = null;
        if (invoice.ExternalInvoiceId != null)
        {
            try
            {
                paypalPaymentId = await _paypal.RecordExternalPaymentAsync(invoice.ExternalInvoiceId, input.PaidOn, input.Method, input.Amount, invoice.Currency, $"{input.Method}: {reference}", ct).ConfigureAwait(false);
            }
            catch (PaymentProviderException e)
            {
                return await FailedAsync(actor, "payment.record-failed", invoice, e, "PayPal is not answering right now. Nothing was recorded; please try again in a few minutes.", ct).ConfigureAwait(false);
            }
        }

        var paidUtc = DateTime.SpecifyKind(input.PaidOn.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
        var payment = Payment.Record(invoice.TenantId, invoice.SubscriptionId, PaymentSource.Offline, reference, input.Amount, invoice.Currency, paidUtc, actor, invoice.Id, input.Method);
        await _store.AddPaymentAsync(payment, ct).ConfigureAwait(false);
        await ApplyAmountsAsync(invoice, extra: payment, ct).ConfigureAwait(false);
        await AuditAsync(actor, "payment.recorded-offline", invoice,
            $"{invoice.Number}: {Money(input.Amount, invoice.Currency)} by {input.Method} on {input.PaidOn:yyyy-MM-dd}, reference {reference}{(paypalPaymentId == null ? "" : $", PayPal {paypalPaymentId}")}", ct).ConfigureAwait(false);
        await _store.SaveAsync(ct).ConfigureAwait(false);
        return new InvoiceResult(true, id, null);
    }

    /// <summary>
    /// Gives money back: through PayPal for a PayPal payment (a subscription sale or an invoice
    /// capture), or recorded for an offline payment returned offline. Owners any amount; Billing up
    /// to the refund limit. Safe to repeat: PayPal refunds once per request id.
    /// </summary>
    public async Task<InvoiceResult> RefundAsync(Guid paymentId, decimal amount, string reason, string actor, bool isOwner, CancellationToken ct = default)
    {
        var payment = await _store.FindPaymentAsync(paymentId, ct).ConfigureAwait(false);
        if (payment == null) return InvoiceResult.Fail("Payment not found.");
        if (!isOwner && amount > _options.RefundLimit)
            return InvoiceResult.Fail(_options.RefundLimit == 0
                ? "Only a Provider Owner can refund."
                : $"Billing users can refund up to {Money(_options.RefundLimit, payment.Currency)} at once; ask a Provider Owner for more.");
        if (payment.Status == PaymentStatus.Reversed) return InvoiceResult.Fail("This payment was reversed (a chargeback): nothing is left to refund.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount) return InvoiceResult.Fail("Give an amount above 0 with at most two decimals.");
        if (amount > payment.Amount - payment.RefundedAmount) return InvoiceResult.Fail($"At most {Money(payment.Amount - payment.RefundedAmount, payment.Currency)} is left to refund.");
        var text = (reason ?? "").Trim();
        if (text.Length is 0 or > PaymentRefund.MaxReasonLength) return InvoiceResult.Fail($"Give a reason (up to {PaymentRefund.MaxReasonLength} characters).");
        var invoice = payment.InvoiceId == null ? null : await _store.FindAsync(payment.InvoiceId.Value, ct).ConfigureAwait(false);

        string? refundId = null;
        var status = RefundStatus.Completed;
        if (payment.Source == PaymentSource.PayPal)
        {
            // The same refund asked twice (a double click, a retry) has the same request id: PayPal refunds once.
            var requestId = $"rf-{payment.Id:N}-{payment.RefundedAmount.ToString("0.00", CultureInfo.InvariantCulture)}-{amount.ToString("0.00", CultureInfo.InvariantCulture)}";
            try
            {
                var refund = await _paypal.RefundAsync(payment.InvoiceId == null ? RefundTarget.SubscriptionSale : RefundTarget.InvoiceCapture,
                    payment.ExternalId!, amount, payment.Currency, requestId, text, ct).ConfigureAwait(false);
                refundId = refund.Id;
                status = refund.Status is "PENDING" ? RefundStatus.Pending : refund.Status is "FAILED" or "CANCELLED" ? RefundStatus.Failed : RefundStatus.Completed;
            }
            catch (PaymentProviderException e)
            {
                await _store.AddAuditAsync(AuditEntry.Record(actor, "payment.refund-failed", payment.Id.ToString(), $"{Money(amount, payment.Currency)}: PayPal {e.Message}{Debug(e)}", _clock.UtcNow, payment.TenantId), ct).ConfigureAwait(false);
                await _store.SaveAsync(ct).ConfigureAwait(false);
                return InvoiceResult.Fail("PayPal refused or did not answer. Nothing was refunded; check the payment in PayPal, then try again.");
            }
            if (await _store.RefundExistsAsync(refundId, ct).ConfigureAwait(false)) return new InvoiceResult(true, invoice?.Id, null, "PayPal had already made this refund.");
        }
        else if (invoice?.ExternalInvoiceId != null)
        {
            try
            {
                refundId = await _paypal.RecordExternalRefundAsync(invoice.ExternalInvoiceId, DateOnly.FromDateTime(_clock.UtcNow), payment.Method ?? OfflinePaymentMethod.Other, amount, payment.Currency, ct).ConfigureAwait(false);
            }
            catch (PaymentProviderException e)
            {
                return await FailedAsync(actor, "payment.refund-failed", invoice, e, "PayPal is not answering right now. Nothing was recorded; please try again in a few minutes.", ct).ConfigureAwait(false);
            }
        }

        var row = PaymentRefund.Record(payment, refundId, amount, status, text, _clock.UtcNow, actor);
        await _store.AddRefundAsync(row, ct).ConfigureAwait(false);
        var refunds = (await _store.RefundsForPaymentsAsync(new[] { payment.Id }, ct).ConfigureAwait(false)).Append(row);
        payment.SetRefundedTotal(refunds.Where(r => r.Status != RefundStatus.Failed).Sum(r => r.Amount), _clock.UtcNow);
        if (invoice != null) await ApplyAmountsAsync(invoice, extra: null, ct).ConfigureAwait(false);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "payment.refunded", payment.Id.ToString(),
            $"{Money(amount, payment.Currency)} of {payment.ExternalId} ({payment.Source}{(invoice == null ? "" : $", {invoice.Number}")}): {text}{(refundId == null ? "" : $"; refund {refundId}")}{(status == RefundStatus.Completed ? "" : $"; {status}")}",
            _clock.UtcNow, payment.TenantId), ct).ConfigureAwait(false);
        await _store.SaveAsync(ct).ConfigureAwait(false);
        return new InvoiceResult(true, invoice?.Id, null, status == RefundStatus.Pending ? "PayPal accepted the refund; it is still pending." : null);
    }

    // ------------------------------------------------------------------ helpers

    private Invoice Build(NewInvoiceInput input, string number, string actor)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        if (input.DueDate < today) throw new ArgumentException("The due date cannot be in the past.", nameof(input.DueDate));
        return Invoice.Create(input.TenantId, null, input.Source, number, input.Currency,
            (input.Lines ?? Array.Empty<InvoiceLineInput>()).Where(l => !string.IsNullOrWhiteSpace(l.Description)).Select(l => (l.Description, l.Quantity, l.UnitAmount)),
            today, input.DueDate, null, null, input.Notes, actor, _clock.UtcNow);
    }

    /// <summary>Sends through PayPal or by email; returns a result only when something went wrong (null = sent).</summary>
    private async Task<InvoiceResult?> SendAsync(Invoice invoice, string actor, bool first, CancellationToken ct)
    {
        var tenantName = await _store.TenantNameAsync(invoice.TenantId, ct).ConfigureAwait(false) ?? "";
        var owners = await _store.OwnerEmailsAsync(invoice.TenantId, ct).ConfigureAwait(false);
        if (invoice.Source == InvoiceSource.Manual)
        {
            if (first) invoice.MarkSent(null, _clock.UtcNow);
            await AuditAsync(actor, first ? "invoice.sent" : "invoice.resent", invoice, $"{invoice.Number}: emailed to the owners (offline payment)", ct).ConfigureAwait(false);
            await _store.SaveAsync(ct).ConfigureAwait(false);
            var undelivered = await EmailAsync(invoice, tenantName, owners, ct).ConfigureAwait(false);
            return undelivered ? new InvoiceResult(true, invoice.Id, null, "The invoice is issued, but the email to the owners could not be sent. Send it again later.") : null;
        }

        try
        {
            if (invoice.ExternalInvoiceId == null)
            {
                var externalId = await _paypal.CreateDraftAsync(new NewPayPalInvoice(invoice.Number, invoice.Currency, invoice.IssueDate, invoice.DueDate,
                    owners[0], null, null, tenantName, invoice.Lines.Select(l => new PayPalInvoiceLine(l.Description, l.Quantity, l.UnitAmount)).ToList(),
                    invoice.Notes, invoice.Number), ct).ConfigureAwait(false);
                invoice.CreatedAtPayPal(externalId);
                await _store.SaveAsync(ct).ConfigureAwait(false); // kept even if the send below fails: a retry sends this draft
            }
            var payerPage = await _paypal.SendAsync(invoice.ExternalInvoiceId!, ct).ConfigureAwait(false);
            invoice.MarkSent(payerPage, _clock.UtcNow);
        }
        catch (PaymentProviderException e)
        {
            return await FailedAsync(actor, first ? "invoice.send-failed" : "invoice.resend-failed", invoice, e,
                first ? "The invoice is saved as a draft, but PayPal is not answering right now. Open it and click \"Send\" again in a few minutes."
                      : "PayPal is not answering right now; please try again in a few minutes.", ct).ConfigureAwait(false);
        }
        await AuditAsync(actor, first ? "invoice.sent" : "invoice.resent", invoice, $"{invoice.Number}: sent by PayPal ({invoice.ExternalInvoiceId})", ct).ConfigureAwait(false);
        await _store.SaveAsync(ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>Emails an offline invoice to the owners; true when an email could not be delivered (audited).</summary>
    private async Task<bool> EmailAsync(Invoice invoice, string tenantName, IReadOnlyList<string> owners, CancellationToken ct)
    {
        var lines = string.Concat(invoice.Lines.Select(l =>
            $"<tr><td>{WebUtility.HtmlEncode(l.Description)}</td><td style=\"text-align:right\">{l.Quantity:0.##}</td><td style=\"text-align:right\">{Money(l.UnitAmount, invoice.Currency)}</td><td style=\"text-align:right\">{Money(l.Amount, invoice.Currency)}</td></tr>"));
        var html = $"<p>Invoice {invoice.Number} for {WebUtility.HtmlEncode(tenantName)}, issued {invoice.IssueDate:d MMMM yyyy}, due {invoice.DueDate:d MMMM yyyy}.</p>"
            + $"<table cellpadding=\"4\"><tr><th align=\"left\">Item</th><th>Quantity</th><th>Price</th><th>Amount</th></tr>{lines}<tr><td colspan=\"3\"><strong>Total</strong></td><td style=\"text-align:right\"><strong>{Money(invoice.Total, invoice.Currency)}</strong></td></tr></table>"
            + (invoice.Notes == null ? "" : $"<p>{WebUtility.HtmlEncode(invoice.Notes)}</p>")
            + "<p>Please quote the invoice number with your payment.</p>";
        var failed = false;
        foreach (var to in owners)
        {
            try
            {
                await _email.SendAsync(to, $"Invoice {invoice.Number} from Inprem", html, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed = true;
                await _store.AddAuditAsync(AuditEntry.Record("system", "notification.failed", invoice.Id.ToString(), $"Invoice {invoice.Number}: {e.GetType().Name}", _clock.UtcNow, invoice.TenantId), ct).ConfigureAwait(false);
                await _store.SaveAsync(ct).ConfigureAwait(false);
            }
        }
        return failed || owners.Count == 0;
    }

    /// <summary>The invoice's paid and refunded totals from its payments (with one not saved yet).</summary>
    private async Task ApplyAmountsAsync(Invoice invoice, Payment? extra, CancellationToken ct)
    {
        var payments = (await _store.PaymentsForInvoiceAsync(invoice.Id, ct).ConfigureAwait(false)).ToList();
        if (extra != null && payments.All(p => p.Id != extra.Id)) payments.Add(extra);
        var paid = payments.Where(p => p.Status != PaymentStatus.Reversed).Sum(p => p.Amount);
        var refunded = payments.Where(p => p.Status != PaymentStatus.Reversed).Sum(p => p.RefundedAmount);
        invoice.ApplyAmounts(paid, Math.Min(refunded, paid), _clock.UtcNow);
    }

    private async Task<InvoiceResult> FailedAsync(string actor, string action, Invoice invoice, PaymentProviderException e, string message, CancellationToken ct)
    {
        await AuditAsync(actor, action, invoice, $"{invoice.Number}: PayPal {e.Message}{Debug(e)}", ct).ConfigureAwait(false);
        await _store.SaveAsync(ct).ConfigureAwait(false);
        return InvoiceResult.Fail(message, invoice.Id);
    }

    private Task AuditAsync(string actor, string action, Invoice invoice, string details, CancellationToken ct) =>
        _store.AddAuditAsync(AuditEntry.Record(actor, action, invoice.Id.ToString(), details, _clock.UtcNow, invoice.TenantId), ct);

    private static string Debug(PaymentProviderException e) =>
        (e.Issue == null ? "" : $" [{e.Issue}]") + (e.DebugId == null ? "" : $" (PayPal debug id {e.DebugId})");

    private static string Plain(ArgumentException e) =>
        e.ParamName == null ? e.Message : e.Message.Replace($" (Parameter '{e.ParamName}')", "", StringComparison.Ordinal);

    public static string Money(decimal amount, string currency) => $"{amount.ToString("#,0.00", CultureInfo.InvariantCulture)} {currency}";
}
