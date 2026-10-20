using System.Globalization;
using System.Text.Json;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

public interface IBillingEventStore
{
    /// <summary>Stores a new event; false if an event with this PayPal id is already stored (a duplicate delivery).</summary>
    Task<bool> TryAddAsync(BillingEvent billingEvent, CancellationToken ct = default);

    Task<BillingEvent?> FindAsync(string externalEventId, CancellationToken ct = default);

    /// <summary>Newest first, filtered by status and event type (prefix).</summary>
    Task<(IReadOnlyList<BillingEvent> Items, int Total)> ListAsync(BillingEventStatus? status, string? eventType, int skip, int take, CancellationToken ct = default);

    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Hands a stored event to the background jobs (Hangfire in the web app).</summary>
public interface IBillingEventQueue
{
    void Enqueue(string externalEventId);
}

/// <summary>What the webhook endpoint answers PayPal.</summary>
public enum WebhookOutcome
{
    /// <summary>Verified, stored and queued (200).</summary>
    Accepted,

    /// <summary>Verified, already stored: nothing done (200).</summary>
    Duplicate,

    /// <summary>Not verified or not an event (400, logged as suspicious).</summary>
    Rejected,
}

/// <summary>
/// PayPal webhooks (Phase 2 step 2.5, design 8): verify with PayPal, store once by PayPal's
/// event id, answer at once, process in a background job that re-fetches from PayPal.
/// </summary>
public sealed class BillingWebhookService
{
    private readonly IPaymentProvider _paypal;
    private readonly IBillingEventStore _events;
    private readonly IBillingEventQueue _queue;
    private readonly IClock _clock;

    public BillingWebhookService(IPaymentProvider paypal, IBillingEventStore events, IBillingEventQueue queue, IClock clock)
    {
        _paypal = paypal;
        _events = events;
        _queue = queue;
        _clock = clock;
    }

    public async Task<WebhookOutcome> ReceiveAsync(WebhookMessage message, CancellationToken ct = default)
    {
        if (!await _paypal.VerifyWebhookAsync(message, ct).ConfigureAwait(false)) return WebhookOutcome.Rejected;

        string? id, type, resourceType, resourceId;
        try
        {
            using var json = JsonDocument.Parse(message.Body);
            var root = json.RootElement;
            id = Text(root, "id");
            type = Text(root, "event_type");
            resourceType = Text(root, "resource_type");
            resourceId = root.TryGetProperty("resource", out var resource) ? Text(resource, "id") : null;
        }
        catch (JsonException)
        {
            return WebhookOutcome.Rejected;
        }
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type) || id.Length > 100 || type.Length > 100) return WebhookOutcome.Rejected;

        if (!await _events.TryAddAsync(BillingEvent.Receive(id, type, resourceType, resourceId, message.Body, _clock.UtcNow), ct).ConfigureAwait(false))
            return WebhookOutcome.Duplicate;
        _queue.Enqueue(id);
        return WebhookOutcome.Accepted;
    }

    /// <summary>Console "Replay": process a stored event again (safe: processing is idempotent).</summary>
    public async Task<bool> ReplayAsync(string externalEventId, string actor, CancellationToken ct = default)
    {
        var billingEvent = await _events.FindAsync(externalEventId, ct).ConfigureAwait(false);
        if (billingEvent == null) return false;
        await _events.AddAuditAsync(AuditEntry.Record(actor, "billing-event.replayed", billingEvent.ExternalEventId, $"{billingEvent.EventType} ({billingEvent.Status}, {billingEvent.Attempts} attempts)", _clock.UtcNow), ct).ConfigureAwait(false);
        await _events.SaveChangesAsync(ct).ConfigureAwait(false);
        _queue.Enqueue(billingEvent.ExternalEventId);
        return true;
    }

    internal static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Processes one stored PayPal event (the Hangfire job). PayPal's API is the source of truth:
/// subscription events re-fetch the subscription and apply its current state, so duplicates and
/// events arriving out of order end in the same place. Payments are recorded once per sale id.
/// A failure is recorded on the event and rethrown, so the job retries and the console shows it.
/// </summary>
public sealed class BillingEventProcessor
{
    private readonly IBillingEventStore _events;
    private readonly ICheckoutStore _store;
    private readonly IPaymentProvider _paypal;
    private readonly CheckoutService _checkout;
    private readonly SubscriptionSync _sync;
    private readonly InvoiceSync _invoices;
    private readonly IClock _clock;

    public BillingEventProcessor(IBillingEventStore events, ICheckoutStore store, IPaymentProvider paypal, CheckoutService checkout, SubscriptionSync sync, InvoiceSync invoices, IClock clock)
    {
        _invoices = invoices;
        _events = events;
        _store = store;
        _paypal = paypal;
        _checkout = checkout;
        _sync = sync;
        _clock = clock;
    }

    public async Task ProcessAsync(string externalEventId, CancellationToken ct = default)
    {
        var billingEvent = await _events.FindAsync(externalEventId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"No billing event {externalEventId}.");
        billingEvent.StartAttempt();
        await _events.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            using var json = JsonDocument.Parse(billingEvent.RawJson);
            var resource = json.RootElement.TryGetProperty("resource", out var r) ? r : default;
            var type = billingEvent.EventType;

            if (type.StartsWith("BILLING.SUBSCRIPTION.", StringComparison.Ordinal))
                await SubscriptionEventAsync(type, BillingWebhookService.Text(resource, "id"), ct).ConfigureAwait(false);
            else if (type == "PAYMENT.SALE.COMPLETED")
                await SaleCompletedAsync(resource, ct).ConfigureAwait(false);
            else if (type == "PAYMENT.SALE.REFUNDED")
                await RefundAsync(BillingWebhookService.Text(resource, "sale_id"), resource, ct).ConfigureAwait(false);
            else if (type == "PAYMENT.SALE.REVERSED")
                await SaleReturnedAsync(type, resource, ct).ConfigureAwait(false);
            else if (type.StartsWith("INVOICING.INVOICE.", StringComparison.Ordinal))
            {
                var invoiceId = BillingWebhookService.Text(resource, "id");
                if (!string.IsNullOrWhiteSpace(invoiceId)) await _invoices.SyncAsync(invoiceId, $"paypal:webhook {type}", ct).ConfigureAwait(false);
            }
            else if (type == "PAYMENT.CAPTURE.REFUNDED")
                await RefundAsync(CaptureOf(resource), resource, ct).ConfigureAwait(false);
            else if (type == "PAYMENT.CAPTURE.REVERSED")
            {
                var capture = CaptureOf(resource) ?? BillingWebhookService.Text(resource, "id");
                if (!string.IsNullOrWhiteSpace(capture)) await _invoices.CaptureReversedAsync(capture, "paypal:webhook PAYMENT.CAPTURE.REVERSED", ct).ConfigureAwait(false);
            }
            // Other event types are stored and marked processed: nothing to apply.

            var processed = await _events.FindAsync(externalEventId, ct).ConfigureAwait(false) ?? billingEvent;
            processed.Processed(_clock.UtcNow);
            await _events.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _store.Reset();
            var failed = await _events.FindAsync(externalEventId, ct).ConfigureAwait(false);
            if (failed != null)
            {
                failed.Failed($"{e.GetType().Name}: {e.Message}");
                await _events.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            throw;
        }
    }

    private async Task SubscriptionEventAsync(string type, string? subscriptionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId)) return;

        // Re-fetch first: if PayPal cannot be reached, the job fails and retries (the confirm below
        // treats an unreachable PayPal as "nothing new", which would lose the event).
        await _paypal.GetSubscriptionAsync(subscriptionId, ct).ConfigureAwait(false);

        // A checkout still waiting for this subscription: the checkout's own confirm (design 7.5 source 2).
        var checkout = await _store.FindByExternalIdAsync(subscriptionId, ct).ConfigureAwait(false);
        if (checkout is { IsFinal: false })
        {
            await _checkout.ConfirmAsync(checkout.Id, CheckoutSource.Webhook, ct).ConfigureAwait(false);
            if (type == "BILLING.SUBSCRIPTION.ACTIVATED") return;
        }

        await _sync.SyncAsync(subscriptionId, $"paypal:webhook {type}", ct).ConfigureAwait(false);
    }

    private async Task SaleCompletedAsync(JsonElement sale, CancellationToken ct)
    {
        var saleId = BillingWebhookService.Text(sale, "id");
        var subscriptionId = BillingWebhookService.Text(sale, "billing_agreement_id");
        if (string.IsNullOrWhiteSpace(saleId) || string.IsNullOrWhiteSpace(subscriptionId)) return;

        var tenant = await _store.TenantWithExternalIdAsync(subscriptionId, ct).ConfigureAwait(false);
        if (tenant == null) return; // not one of ours (or not linked yet: reconciliation and the next payment catch up)

        await _store.WithLockAsync(BillingLocks.Tenant(tenant.Value), async () =>
        {
            _store.Reset();
            if (await _store.FindPaymentAsync(PaymentSource.PayPal, saleId, ct).ConfigureAwait(false) == null)
            {
                var (amount, currency) = Money(sale);
                var subscription = await _store.FindSubscriptionAsync(tenant.Value, ct).ConfigureAwait(false);
                var paid = Time(BillingWebhookService.Text(sale, "create_time")) ?? _clock.UtcNow;
                await _store.AddPaymentAsync(Payment.Record(tenant.Value, subscription?.Id, PaymentSource.PayPal, saleId, amount, currency, paid, "paypal:webhook"), ct).ConfigureAwait(false);
                await _store.AddAuditAsync(AuditEntry.Record("paypal:webhook", "payment.received", saleId, $"{amount:0.00} {currency} (PayPal {subscriptionId})", _clock.UtcNow, tenant), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return true;
        }, ct).ConfigureAwait(false);

        // The period moves on and PastDue clears: from PayPal's current view of the subscription.
        await _sync.SyncAsync(subscriptionId, "paypal:webhook PAYMENT.SALE.COMPLETED", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A refund (step 4.3): the resource is the refund (its id, amount and state or status), of a sale
    /// (<c>sale_id</c>) or a capture (the "up" link). Recorded once per refund id, so partial refunds add up.
    /// </summary>
    private async Task RefundAsync(string? paymentExternalId, JsonElement refund, CancellationToken ct)
    {
        var refundId = BillingWebhookService.Text(refund, "id");
        if (string.IsNullOrWhiteSpace(paymentExternalId) || string.IsNullOrWhiteSpace(refundId)) return;
        var state = (BillingWebhookService.Text(refund, "status") ?? BillingWebhookService.Text(refund, "state") ?? "COMPLETED").ToUpperInvariant();
        var status = state == "PENDING" ? RefundStatus.Pending : state is "FAILED" or "CANCELLED" ? RefundStatus.Failed : RefundStatus.Completed;
        await _invoices.RefundReportedAsync(paymentExternalId, refundId, Money(refund).Amount, status, "paypal:webhook", ct).ConfigureAwait(false);
    }

    /// <summary>The capture a v2 refund belongs to: its "up" link (<c>…/v2/payments/captures/{id}</c>).</summary>
    private static string? CaptureOf(JsonElement resource)
    {
        if (!resource.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) return null;
        foreach (var link in links.EnumerateArray())
        {
            var href = BillingWebhookService.Text(link, "href");
            if (BillingWebhookService.Text(link, "rel") == "up" && href != null && href.Contains("/captures/", StringComparison.Ordinal))
                return href.TrimEnd('/').Split('/').Last();
        }
        return null;
    }

    private async Task SaleReturnedAsync(string type, JsonElement resource, CancellationToken ct)
    {
        // REVERSED (a chargeback): the resource is the sale. (Refunds go through RefundAsync.)
        var saleId = BillingWebhookService.Text(resource, "id");
        if (string.IsNullOrWhiteSpace(saleId)) return;
        var payment = await _store.FindPaymentAsync(PaymentSource.PayPal, saleId, ct).ConfigureAwait(false);
        if (payment == null) return; // a payment we never recorded: nothing to change

        await _store.WithLockAsync(BillingLocks.Tenant(payment.TenantId), async () =>
        {
            _store.Reset();
            var current = (await _store.FindPaymentAsync(PaymentSource.PayPal, saleId, ct).ConfigureAwait(false))!;
            if (current.Reverse(_clock.UtcNow))
            {
                await _store.AddAuditAsync(AuditEntry.Record("paypal:webhook", "payment.reversed", saleId,
                    $"{current.RefundedAmount:0.00} {current.Currency} of {current.Amount:0.00}", _clock.UtcNow, current.TenantId), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return true;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>A v1 sale or refund amount: <c>{"total": "29.00", "currency": "USD"}</c> (or v2's value/currency_code).</summary>
    private static (decimal Amount, string Currency) Money(JsonElement resource)
    {
        if (!resource.TryGetProperty("amount", out var amount)) throw new FormatException("The event has no amount.");
        var total = BillingWebhookService.Text(amount, "total") ?? BillingWebhookService.Text(amount, "value");
        var currency = BillingWebhookService.Text(amount, "currency") ?? BillingWebhookService.Text(amount, "currency_code");
        if (!decimal.TryParse(total, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || currency == null) throw new FormatException("The event's amount cannot be read.");
        return (value, currency);
    }

    private static DateTime? Time(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time.UtcDateTime : null;
}

/// <summary>One lock per organization for everything that changes its billing (checkout, webhooks, reconciliation).</summary>
public static class BillingLocks
{
    public static string Tenant(int tenantId) => $"billing:tenant:{tenantId}";

    public static string For(CheckoutSession session) => session.TenantId is int tenant ? Tenant(tenant) : $"billing:checkout:{session.Id:N}";
}

/// <summary>
/// Brings an organization's subscription in line with what PayPal reports now (webhooks, step
/// 2.5; daily reconciliation, step 2.6). Only moves the state diagram has; each change audited.
/// </summary>
public sealed class SubscriptionSync
{
    private readonly ICheckoutStore _store;
    private readonly IPaymentProvider _paypal;
    private readonly IClock _clock;
    private readonly Notifications.BillingAlerts? _alerts;

    public SubscriptionSync(ICheckoutStore store, IPaymentProvider paypal, IClock clock, Notifications.BillingAlerts? alerts = null)
    {
        _store = store;
        _paypal = paypal;
        _clock = clock;
        _alerts = alerts;
    }

    /// <summary>Re-reads the PayPal subscription and applies it; returns what changed (null: nothing, or not ours).</summary>
    public async Task<string?> SyncAsync(string subscriptionId, string actor, CancellationToken ct = default)
    {
        var tenant = await _store.TenantWithExternalIdAsync(subscriptionId, ct).ConfigureAwait(false);
        if (tenant == null) return null;
        var paypal = await _paypal.GetSubscriptionAsync(subscriptionId, ct).ConfigureAwait(false);
        if (paypal == null) return null;

        var outstanding = 0m;
        var action = await _store.WithLockAsync(BillingLocks.Tenant(tenant.Value), async () =>
        {
            _store.Reset();
            var subscription = await _store.FindSubscriptionAsync(tenant.Value, ct).ConfigureAwait(false);
            if (subscription == null || subscription.ExternalSubscriptionId != subscriptionId) return null;
            var change = await ApplyAsync(subscription, paypal, ct).ConfigureAwait(false);
            if (change != null)
            {
                await _store.AddAuditAsync(AuditEntry.Record(actor, change.Value.Action, subscription.Id.ToString(), change.Value.Details, _clock.UtcNow, subscription.TenantId), ct).ConfigureAwait(false);
            }
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            outstanding = subscription.OutstandingBalance;
            return change?.Action;
        }, ct).ConfigureAwait(false);

        // Phase 5 step 5.3: after the change is saved, the owners and Inprem hear about it (an alert never undoes the change).
        if (_alerts != null && action == "subscription.past-due") await _alerts.PaymentFailedAsync(tenant.Value, outstanding, _clock.UtcNow, ct).ConfigureAwait(false);
        if (_alerts != null && action == "subscription.suspended") await _alerts.SuspendedAsync(tenant.Value, outstanding, _clock.UtcNow, ct).ConfigureAwait(false);
        return action;
    }

    private async Task<(string Action, string Details)?> ApplyAsync(Subscription s, PayPalSubscription p, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var period = $"next billing {p.NextBillingUtc:yyyy-MM-dd}";
        switch (p.State)
        {
            case PayPalSubscriptionState.Active:
                var price = p.PlanId == null ? null : await _store.FindPriceByPayPalPlanAsync(p.PlanId, ct).ConfigureAwait(false);
                if (s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
                {
                    var planChanged = price != null && s.ChangePrice(price);
                    if (p.OutstandingBalance > 0 && p.FailedPaymentsCount > 0)
                    {
                        if (s.MarkPastDue(p.OutstandingBalance, now)) return ("subscription.past-due", $"PayPal could not collect {p.OutstandingBalance:0.00}; PayPal retries ({p.FailedPaymentsCount} failed).");
                        s.Refresh(null, p.NextBillingUtc, p.OutstandingBalance);
                    }
                    else if (s.Status == SubscriptionStatus.PastDue)
                    {
                        s.RecordPayment(p.LastPaymentUtc, p.NextBillingUtc, now);
                        return ("subscription.payment-recovered", $"Paid; {period}.");
                    }
                    else
                    {
                        s.Refresh(p.LastPaymentUtc, p.NextBillingUtc, 0m);
                    }
                    return planChanged ? ("subscription.plan-changed", $"PayPal plan {p.PlanId}") : null;
                }
                if (price == null) return ("subscription.sync-refused", $"PayPal says ACTIVE on plan {p.PlanId}, which is not in our catalogue: not activated.");
                if (s.Status is SubscriptionStatus.Closed) return null;
                var wasSuspended = s.Status == SubscriptionStatus.Suspended;
                return s.Activate(p.Id, price, p.LastPaymentUtc ?? p.StartUtc, p.NextBillingUtc, now)
                    ? (wasSuspended ? "subscription.reactivated" : "subscription.activated", $"PayPal: ACTIVE; {period}.")
                    : null;

            case PayPalSubscriptionState.Suspended:
                return s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue && s.Suspend(now)
                    ? ("subscription.suspended", $"PayPal suspended the subscription (balance {p.OutstandingBalance:0.00}). Read-only until paid.")
                    : null;

            case PayPalSubscriptionState.Cancelled:
                if (s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.PendingActivation && s.Cancel(now))
                    return ("subscription.canceled", $"Cancelled in PayPal; access until {s.CurrentPeriodEndUtc:yyyy-MM-dd}.");
                return null;

            case PayPalSubscriptionState.Expired:
                return s.Status is not (SubscriptionStatus.Expired or SubscriptionStatus.Closed or SubscriptionStatus.Trialing) && s.ExpireSubscription(now)
                    ? ("subscription.expired", "PayPal: the subscription expired. Read-only.")
                    : null;

            default:
                return null; // APPROVAL_PENDING / APPROVED: the checkout handles these
        }
    }
}
