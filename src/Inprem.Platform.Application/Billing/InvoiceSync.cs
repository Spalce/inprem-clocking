using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

/// <summary>
/// Brings invoices and refunds in line with PayPal (Phase 4 step 4.3): from webhooks
/// (<c>INVOICING.INVOICE.*</c>, <c>PAYMENT.SALE.REFUNDED</c>, <c>PAYMENT.CAPTURE.REFUNDED</c> and
/// <c>.REVERSED</c>) and the daily check. PayPal is re-read where it can be, and every record is
/// keyed by PayPal's own id (payment, capture, refund), so repeated or late messages change nothing.
/// Offline payments are recorded only from the console; PayPal's "external" lines are ours already.
/// </summary>
public sealed class InvoiceSync
{
    private readonly IInvoiceStore _store;
    private readonly ICheckoutStore _locks;
    private readonly IInvoiceProvider _paypal;
    private readonly IClock _clock;

    public InvoiceSync(IInvoiceStore store, ICheckoutStore locks, IInvoiceProvider paypal, IClock clock)
    {
        _store = store;
        _locks = locks;
        _paypal = paypal;
        _clock = clock;
    }

    /// <summary>Applies what PayPal says about one invoice now; returns what changed, or null.</summary>
    public async Task<string?> SyncAsync(string externalInvoiceId, string actor, CancellationToken ct = default)
    {
        var known = await _store.FindByExternalIdAsync(externalInvoiceId, ct).ConfigureAwait(false);
        if (known == null) return null; // not one of ours
        var info = await _paypal.GetAsync(externalInvoiceId, ct).ConfigureAwait(false); // unreachable: throws, the job retries
        if (info == null) return null;

        return await _locks.WithLockAsync(BillingLocks.Tenant(known.TenantId), async () =>
        {
            _locks.Reset();
            var invoice = (await _store.FindByExternalIdAsync(externalInvoiceId, ct).ConfigureAwait(false))!;
            var changes = new List<string>();

            if (invoice.Status == InvoiceStatus.Draft && info.State is not (PayPalInvoiceState.Draft or PayPalInvoiceState.Cancelled or PayPalInvoiceState.Unknown) && info.PayerUrl != null)
            {
                invoice.MarkSent(info.PayerUrl, _clock.UtcNow);
                changes.Add("sent");
                await AuditAsync(actor, "invoice.sent", invoice, $"{invoice.Number}: sent (PayPal {info.RawStatus})", ct).ConfigureAwait(false);
            }

            // Payments made through PayPal (by the payer): one per PayPal payment id.
            var newPayments = new List<Payment>();
            foreach (var paid in info.Payments.Where(p => !p.External && !string.IsNullOrWhiteSpace(p.PaymentId) && p.Amount > 0))
            {
                if (await _store.FindPaymentAsync(PaymentSource.PayPal, paid.PaymentId!, ct).ConfigureAwait(false) != null) continue;
                if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled) continue; // never seen in practice; nothing safe to apply
                var when = paid.Date == null ? _clock.UtcNow : DateTime.SpecifyKind(paid.Date.Value.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
                var payment = Payment.Record(invoice.TenantId, invoice.SubscriptionId, PaymentSource.PayPal, paid.PaymentId, paid.Amount, invoice.Currency, when, actor, invoice.Id);
                await _store.AddPaymentAsync(payment, ct).ConfigureAwait(false);
                newPayments.Add(payment);
                changes.Add($"paid {InvoiceService.Money(paid.Amount, invoice.Currency)}");
                await _store.AddAuditAsync(AuditEntry.Record(actor, "payment.received", paid.PaymentId,
                    $"{InvoiceService.Money(paid.Amount, invoice.Currency)} on {invoice.Number} (PayPal invoice {externalInvoiceId})", _clock.UtcNow, invoice.TenantId), ct).ConfigureAwait(false);
            }

            // Refunds PayPal lists on the invoice (made from PayPal's site, or ours): one per refund id.
            var payments = (await _store.PaymentsForInvoiceAsync(invoice.Id, ct).ConfigureAwait(false)).Concat(newPayments).Where(p => p.Source == PaymentSource.PayPal).ToList();
            foreach (var refunded in info.Refunds.Where(r => !r.External && !string.IsNullOrWhiteSpace(r.RefundId) && r.Amount > 0))
            {
                if (await _store.FindRefundAsync(refunded.RefundId!, ct).ConfigureAwait(false) != null) continue;
                var target = payments.FirstOrDefault(p => p.Status != PaymentStatus.Reversed && p.Amount - p.RefundedAmount >= refunded.Amount);
                if (target == null) continue; // cannot be matched to one payment: the monthly report shows it
                changes.Add(await AddRefundAsync(target, refunded.RefundId!, refunded.Amount, RefundStatus.Completed, "Refunded at PayPal", actor, ct).ConfigureAwait(false));
            }

            if (info.State == PayPalInvoiceState.Cancelled && invoice.Status != InvoiceStatus.Cancelled && invoice.PaidAmount == 0 && newPayments.Count == 0)
            {
                invoice.Cancel("Cancelled at PayPal", _clock.UtcNow);
                changes.Add("cancelled");
                await AuditAsync(actor, "invoice.cancelled", invoice, $"{invoice.Number}: cancelled at PayPal", ct).ConfigureAwait(false);
            }

            if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled)) await ApplyAmountsAsync(invoice, newPayments, ct).ConfigureAwait(false);
            await _store.SaveAsync(ct).ConfigureAwait(false);
            return changes.Count == 0 ? null : $"{invoice.Number}: {string.Join(", ", changes)}";
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A refund PayPal reports (a webhook): recorded once by PayPal's refund id, against the PayPal
    /// payment (sale or capture) it refunds. A refund made from our console is already recorded.
    /// </summary>
    public async Task<string?> RefundReportedAsync(string paymentExternalId, string refundId, decimal amount, RefundStatus status, string actor, CancellationToken ct = default)
    {
        var known = await _store.FindPaymentAsync(PaymentSource.PayPal, paymentExternalId, ct).ConfigureAwait(false);
        if (known == null) return null; // a payment we never recorded

        return await _locks.WithLockAsync(BillingLocks.Tenant(known.TenantId), async () =>
        {
            _locks.Reset();
            var existing = await _store.FindRefundAsync(refundId, ct).ConfigureAwait(false);
            var payment = (await _store.FindPaymentAsync(PaymentSource.PayPal, paymentExternalId, ct).ConfigureAwait(false))!;
            string? change;
            if (existing != null)
            {
                if (!existing.SetStatus(status)) return null; // already recorded, nothing new
                await TotalAsync(payment, null, ct).ConfigureAwait(false);
                change = $"refund {refundId}: {status}";
                await _store.AddAuditAsync(AuditEntry.Record(actor, "payment.refund-updated", payment.Id.ToString(), $"{refundId} is now {status}", _clock.UtcNow, payment.TenantId), ct).ConfigureAwait(false);
            }
            else
            {
                if (payment.Status == PaymentStatus.Reversed) return null;
                var left = payment.Amount - payment.RefundedAmount;
                if (left <= 0) return null;
                change = await AddRefundAsync(payment, refundId, Math.Min(amount, left), status, "Refunded at PayPal", actor, ct).ConfigureAwait(false);
            }
            if (payment.InvoiceId != null)
            {
                var invoice = await _store.FindAsync(payment.InvoiceId.Value, ct).ConfigureAwait(false);
                if (invoice != null && invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled)) await ApplyAmountsAsync(invoice, Array.Empty<Payment>(), ct).ConfigureAwait(false);
            }
            await _store.SaveAsync(ct).ConfigureAwait(false);
            return change;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>A chargeback on an invoice payment (<c>PAYMENT.CAPTURE.REVERSED</c>).</summary>
    public async Task<string?> CaptureReversedAsync(string captureId, string actor, CancellationToken ct = default)
    {
        var known = await _store.FindPaymentAsync(PaymentSource.PayPal, captureId, ct).ConfigureAwait(false);
        if (known == null) return null;
        return await _locks.WithLockAsync(BillingLocks.Tenant(known.TenantId), async () =>
        {
            _locks.Reset();
            var payment = (await _store.FindPaymentAsync(PaymentSource.PayPal, captureId, ct).ConfigureAwait(false))!;
            if (!payment.Reverse(_clock.UtcNow)) return null;
            await _store.AddAuditAsync(AuditEntry.Record(actor, "payment.reversed", captureId, $"{InvoiceService.Money(payment.Amount, payment.Currency)} reversed (chargeback)", _clock.UtcNow, payment.TenantId), ct).ConfigureAwait(false);
            if (payment.InvoiceId != null)
            {
                var invoice = await _store.FindAsync(payment.InvoiceId.Value, ct).ConfigureAwait(false);
                if (invoice != null && invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled)) await ApplyAmountsAsync(invoice, Array.Empty<Payment>(), ct).ConfigureAwait(false);
            }
            await _store.SaveAsync(ct).ConfigureAwait(false);
            return $"capture {captureId} reversed";
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The daily check: every PayPal invoice not finished, against PayPal (a dropped webhook is corrected here).</summary>
    public async Task<(IReadOnlyList<string> Corrections, int Failed)> SyncOpenAsync(string actor, CancellationToken ct = default)
    {
        var corrections = new List<string>();
        var failed = 0;
        foreach (var id in await _store.OpenPayPalInvoiceIdsAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var change = await SyncAsync(id, actor, ct).ConfigureAwait(false);
                if (change != null) corrections.Add($"PayPal invoice {id}: {change}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                _locks.Reset();
            }
        }
        return (corrections, failed);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<string> AddRefundAsync(Payment payment, string refundId, decimal amount, RefundStatus status, string reason, string actor, CancellationToken ct)
    {
        var refund = PaymentRefund.Record(payment, refundId, amount, status, reason, _clock.UtcNow, actor);
        await _store.AddRefundAsync(refund, ct).ConfigureAwait(false);
        await TotalAsync(payment, refund, ct).ConfigureAwait(false);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "payment.refunded", payment.Id.ToString(),
            $"{InvoiceService.Money(amount, payment.Currency)} of {payment.ExternalId}: refund {refundId}{(status == RefundStatus.Completed ? "" : $" ({status})")}", _clock.UtcNow, payment.TenantId), ct).ConfigureAwait(false);
        return $"refunded {InvoiceService.Money(amount, payment.Currency)}";
    }

    /// <summary>The payment's refunded total: the sum of its refunds that did not fail (one not saved yet included).</summary>
    private async Task TotalAsync(Payment payment, PaymentRefund? added, CancellationToken ct)
    {
        var refunds = (await _store.RefundsForPaymentsAsync(new[] { payment.Id }, ct).ConfigureAwait(false)).ToList();
        if (added != null && refunds.All(r => r.Id != added.Id)) refunds.Add(added);
        payment.SetRefundedTotal(Math.Min(payment.Amount, refunds.Where(r => r.Status != RefundStatus.Failed).Sum(r => r.Amount)), _clock.UtcNow);
    }

    private async Task ApplyAmountsAsync(Invoice invoice, IReadOnlyCollection<Payment> notSaved, CancellationToken ct)
    {
        var payments = (await _store.PaymentsForInvoiceAsync(invoice.Id, ct).ConfigureAwait(false)).ToList();
        payments.AddRange(notSaved.Where(n => payments.All(p => p.Id != n.Id)));
        var counted = payments.Where(p => p.Status != PaymentStatus.Reversed).ToList();
        var paid = counted.Sum(p => p.Amount);
        invoice.ApplyAmounts(paid, Math.Min(paid, counted.Sum(p => p.RefundedAmount)), _clock.UtcNow);
    }

    private Task AuditAsync(string actor, string action, Invoice invoice, string details, CancellationToken ct) =>
        _store.AddAuditAsync(AuditEntry.Record(actor, action, invoice.Id.ToString(), details, _clock.UtcNow, invoice.TenantId), ct);
}
