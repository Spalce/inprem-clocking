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
}
