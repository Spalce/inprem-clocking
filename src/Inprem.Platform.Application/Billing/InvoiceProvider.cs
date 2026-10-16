using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

/// <summary>A line as sent to PayPal.</summary>
public sealed record PayPalInvoiceLine(string Name, decimal Quantity, decimal UnitAmount);

/// <summary>A draft invoice to create at PayPal. <see cref="Number"/> is ours: PayPal refuses a second invoice with it.</summary>
public sealed record NewPayPalInvoice(
    string Number,
    string Currency,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string RecipientEmail,
    string? RecipientGivenName,
    string? RecipientSurname,
    string? RecipientBusinessName,
    IReadOnlyList<PayPalInvoiceLine> Lines,
    string? Note,
    string? Reference);

/// <summary>An invoice's status at PayPal, grouped (PayPal has 16 values; <c>RawStatus</c> keeps the original).</summary>
public enum PayPalInvoiceState
{
    Unknown,
    Draft,
    Scheduled,
    Sent,
    PaymentPending,
    PartiallyPaid,
    Paid,
    PartiallyRefunded,
    Refunded,
    Cancelled,
}

/// <summary>A payment PayPal lists on an invoice: paid through PayPal, or recorded as external (offline).</summary>
public sealed record PayPalInvoicePayment(string? PaymentId, bool External, DateOnly? Date, decimal Amount, string Currency, string? Method);

/// <summary>A refund PayPal lists on an invoice.</summary>
public sealed record PayPalInvoiceRefund(string? RefundId, bool External, DateOnly? Date, decimal Amount, string Currency);

/// <summary>What PayPal says about an invoice now (the source of truth for webhooks and reconciliation).</summary>
public sealed record PayPalInvoiceInfo(
    string Id,
    string? Number,
    PayPalInvoiceState State,
    string? RawStatus,
    decimal Total,
    string Currency,
    decimal PaidAmount,
    decimal RefundedAmount,
    DateOnly? DueDate,
    string? PayerUrl,
    IReadOnlyList<PayPalInvoicePayment> Payments,
    IReadOnlyList<PayPalInvoiceRefund> Refunds);

/// <summary>What a refund refers to at PayPal.</summary>
public enum RefundTarget
{
    /// <summary>A subscription payment (a PayPal sale, <c>PAYMENT.SALE.*</c>).</summary>
    SubscriptionSale,

    /// <summary>A PayPal invoice payment (a capture, <c>PAYMENT.CAPTURE.*</c>).</summary>
    InvoiceCapture,
}

/// <summary>A refund PayPal accepted: its id and PayPal's status (e.g. COMPLETED or PENDING).</summary>
public sealed record ProviderRefund(string Id, string? Status);

/// <summary>
/// PayPal Invoicing v2 and refunds (Phase 4 step 4.1), through a typed HTTP client: the SDK does
/// not cover them. Calls that PayPal cannot make safe to repeat (send, record payment, record refund)
/// are never retried automatically; see docs/multitenancy/paypal-notes.md.
/// </summary>
public interface IInvoiceProvider
{
    bool IsConfigured { get; }

    /// <summary>
    /// Creates a draft invoice and returns PayPal's id (<c>INV2-…</c>). Safe to call again: when PayPal
    /// already has an invoice with our number, that invoice's id is returned.
    /// </summary>
    Task<string> CreateDraftAsync(NewPayPalInvoice invoice, CancellationToken ct = default);

    /// <summary>Sends the invoice to the recipient (PayPal emails it) and returns the payer's page.</summary>
    Task<string> SendAsync(string invoiceId, CancellationToken ct = default);

    /// <summary>The invoice as PayPal reports it, or null if PayPal does not know it.</summary>
    Task<PayPalInvoiceInfo?> GetAsync(string invoiceId, CancellationToken ct = default);

    /// <summary>Cancels a sent invoice (a draft is deleted); already cancelled counts as done.</summary>
    Task CancelAsync(string invoiceId, string note, CancellationToken ct = default);

    /// <summary>Records an offline payment on the invoice at PayPal; returns PayPal's payment id.</summary>
    Task<string> RecordExternalPaymentAsync(string invoiceId, DateOnly date, OfflinePaymentMethod method, decimal amount, string currency, string? note, CancellationToken ct = default);

    /// <summary>Records an offline refund on the invoice at PayPal; returns PayPal's refund id.</summary>
    Task<string> RecordExternalRefundAsync(string invoiceId, DateOnly date, OfflinePaymentMethod method, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Gives money back through PayPal. <paramref name="requestId"/> makes a retry safe (PayPal refunds once).</summary>
    Task<ProviderRefund> RefundAsync(RefundTarget target, string transactionId, decimal amount, string currency, string requestId, string? note, CancellationToken ct = default);
}
