using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

public enum InvoiceSaveResult
{
    Saved,

    /// <summary>The subscription already has an invoice for that period that is not cancelled (nothing was saved).</summary>
    DuplicatePeriod,
}

/// <summary>Invoices and their numbers (Phase 4 step 4.1).</summary>
public interface IInvoiceStore
{
    /// <summary>The next invoice number from the database sequence (<c>INV-100001</c>, …). A number is never handed out twice.</summary>
    Task<string> NextNumberAsync(CancellationToken ct = default);

    Task AddAsync(Invoice invoice, CancellationToken ct = default);

    /// <summary>The invoice with its lines (tracked).</summary>
    Task<Invoice?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>The invoice with this PayPal id (tracked).</summary>
    Task<Invoice?> FindByExternalIdAsync(string externalInvoiceId, CancellationToken ct = default);

    /// <summary>An organization's invoices, newest first, with their lines (read only).</summary>
    Task<IReadOnlyList<Invoice>> ListForTenantAsync(int tenantId, int max, CancellationToken ct = default);

    /// <summary>Saves the changes; a second invoice for the same subscription period is refused, not saved.</summary>
    Task<InvoiceSaveResult> SaveAsync(CancellationToken ct = default);

    // Console (step 4.2).

    /// <summary>A page of invoices matching the filter, newest first, with organization names.</summary>
    Task<InvoicePage> SearchAsync(InvoiceFilter filter, int pageSize, CancellationToken ct = default);

    /// <summary>The payments recorded against an invoice (tracked), oldest first.</summary>
    Task<IReadOnlyList<Payment>> PaymentsForInvoiceAsync(Guid invoiceId, CancellationToken ct = default);

    Task<Payment?> FindPaymentAsync(Guid paymentId, CancellationToken ct = default);

    /// <summary>The refunds of these payments (tracked), oldest first.</summary>
    Task<IReadOnlyList<PaymentRefund>> RefundsForPaymentsAsync(IReadOnlyCollection<Guid> paymentIds, CancellationToken ct = default);

    /// <summary>True when an offline payment with this reference is already recorded (any organization).</summary>
    Task<bool> OfflineReferenceExistsAsync(string reference, CancellationToken ct = default);

    /// <summary>True when a refund with this PayPal id is already recorded.</summary>
    Task<bool> RefundExistsAsync(string? externalRefundId, CancellationToken ct = default);

    Task AddPaymentAsync(Payment payment, CancellationToken ct = default);
    Task AddRefundAsync(PaymentRefund refund, CancellationToken ct = default);
    Task AddAuditAsync(Inprem.Platform.Domain.Audit.AuditEntry entry, CancellationToken ct = default);

    /// <summary>The organization's name, or null when there is no such organization.</summary>
    Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default);

    /// <summary>The active Owners' emails of an organization (invoices go to them).</summary>
    Task<IReadOnlyList<string>> OwnerEmailsAsync(int tenantId, CancellationToken ct = default);

    // Webhooks and reconciliation (step 4.3).

    /// <summary>A payment by its PayPal (or offline) id (tracked).</summary>
    Task<Payment?> FindPaymentAsync(PaymentSource source, string externalId, CancellationToken ct = default);

    /// <summary>A refund by PayPal's refund id (tracked).</summary>
    Task<PaymentRefund?> FindRefundAsync(string externalRefundId, CancellationToken ct = default);

    /// <summary>PayPal ids of invoices still to be paid or with PayPal payments (the daily check).</summary>
    Task<IReadOnlyList<string>> OpenPayPalInvoiceIdsAsync(CancellationToken ct = default);

    /// <summary>Payments received in [from, to), with organization names (the monthly report).</summary>
    Task<IReadOnlyList<(Payment Payment, string TenantName)>> PaymentsBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Refunds made in [from, to), with their payment and organization name (the monthly report).</summary>
    Task<IReadOnlyList<(PaymentRefund Refund, Payment Payment, string TenantName)>> RefundsBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
