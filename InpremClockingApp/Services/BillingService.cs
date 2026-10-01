using InpremClockingApp.Data;
using InpremClockingApp.Models.Billing;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

// Invoice generation, payment recording, and renewal (see multi-tenancy.md Part 2, Phase 8).
// SuperAdmin-only - every query bypasses the tenant query filter via IgnoreQueryFilters(), the
// same audited pattern TenantAdminService uses, since billing data spans every tenant and the
// caller (a SuperAdmin) has no TenantId of their own for the filter to match against.
public class BillingService
{
    private readonly ApplicationDbContext _db;

    private const int InvoiceDueDays = 14;

    public BillingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Invoice>> GetAllInvoicesAsync()
    {
        return await _db.Invoices.IgnoreQueryFilters()
            .OrderByDescending(i => i.IssuedDate)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<List<Invoice>> GetInvoicesForTenantAsync(int tenantId)
    {
        return await _db.Invoices.IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.IssuedDate)
            .ToListAsync().ConfigureAwait(false);
    }

    public async Task<Invoice?> GetInvoiceByIdAsync(int id)
    {
        return await _db.Invoices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
    }

    // Idempotent per period: if a non-void invoice already exists for the subscription's
    // current period, returns it instead of creating a duplicate - so clicking "Generate" twice,
    // or the daily sweep running after a manual generate, never double-bills the same period.
    public async Task<Invoice> GenerateInvoiceAsync(int tenantId)
    {
        var subscription = await _db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} has no subscription to bill.");

        var existing = await _db.Invoices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.SubscriptionId == subscription.Id
                && i.PeriodStart == subscription.CurrentPeriodStart
                && i.Status != InvoiceStatus.Void)
            .ConfigureAwait(false);
        if (existing != null) return existing;

        var invoice = new Invoice
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = "PENDING",
            PeriodStart = subscription.CurrentPeriodStart,
            PeriodEnd = subscription.CurrentPeriodEnd,
            Amount = subscription.Amount,
            Currency = subscription.Currency,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(InvoiceDueDays),
            Status = InvoiceStatus.Issued,
        };
        await _db.Invoices.AddAsync(invoice).ConfigureAwait(false);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        // InvoiceNumber needs the DB-assigned Id, so it's set in a second save rather than
        // guessed ahead of time with a separate counter query (no concurrency risk this way -
        // the Id is already guaranteed unique).
        invoice.InvoiceNumber = $"INV-{invoice.Id:D6}";
        await _db.SaveChangesAsync().ConfigureAwait(false);

        return invoice;
    }

    public async Task MarkInvoicePaidAsync(int invoiceId, DateTime paidDate, string? paymentMethod, string? paymentReference, string? notes)
    {
        var invoice = await _db.Invoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == invoiceId).ConfigureAwait(false);
        if (invoice == null) return;

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidDate = paidDate;
        invoice.PaymentMethod = paymentMethod;
        invoice.PaymentReference = paymentReference;
        invoice.Notes = notes;

        // Paying the invoice is what actually clears a PastDue/Suspended subscription - see
        // multi-tenancy.md Part 2, decision #10. Enforcement (blocking a Suspended tenant's
        // access) is Phase 9, not this one - this only fixes the billing status.
        var subscription = await _db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId).ConfigureAwait(false);
        if (subscription != null && subscription.Status is SubscriptionStatus.PastDue or SubscriptionStatus.Suspended)
        {
            subscription.Status = SubscriptionStatus.Active;
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task VoidInvoiceAsync(int invoiceId)
    {
        var invoice = await _db.Invoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == invoiceId).ConfigureAwait(false);
        if (invoice == null || invoice.Status == InvoiceStatus.Paid) return;

        invoice.Status = InvoiceStatus.Void;
        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    // Advances the subscription's period forward by one billing cycle and generates the invoice
    // for the new period. Called both by a manual trigger and the daily BillingBackgroundService
    // sweep below - see multi-tenancy.md Part 2, decision #10. Does not touch Status - a
    // Suspended subscription keeps renewing (and accruing invoices) until it's paid or canceled,
    // same as real billing practice.
    public async Task RenewSubscriptionAsync(int subscriptionId)
    {
        var subscription = await _db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId).ConfigureAwait(false);
        if (subscription == null) return;

        var newStart = subscription.CurrentPeriodEnd;
        subscription.CurrentPeriodStart = newStart;
        subscription.CurrentPeriodEnd = subscription.BillingCycle == BillingCycle.Annual
            ? newStart.AddYears(1)
            : newStart.AddMonths(1);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        await GenerateInvoiceAsync(subscription.TenantId).ConfigureAwait(false);
    }

    // Daily sweep, called by BillingBackgroundService: generates upcoming renewal invoices and
    // flags non-payment. Deliberately never touches Tenant.IsActive - wiring a subscription's
    // billing status into an actual access gate is multi-tenancy.md Part 2, Phase 9, not this
    // one. Safe to call repeatedly (e.g. if the job missed a day) - every transition checks the
    // current state first, so re-running it on an already-up-to-date subscription is a no-op.
    public async Task<(int Renewed, int PastDue, int Suspended)> RunDailySweepAsync(int renewalLeadDays = 14, int gracePeriodDays = 14)
    {
        var now = DateTime.UtcNow;
        int renewed = 0, pastDue = 0, suspended = 0;

        var dueForRenewal = await _db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status != SubscriptionStatus.Canceled && s.CurrentPeriodEnd <= now.AddDays(renewalLeadDays))
            .ToListAsync().ConfigureAwait(false);

        foreach (var subscription in dueForRenewal)
        {
            var alreadyRenewed = await _db.Invoices.IgnoreQueryFilters()
                .AnyAsync(i => i.SubscriptionId == subscription.Id
                    && i.PeriodStart == subscription.CurrentPeriodEnd
                    && i.Status != InvoiceStatus.Void)
                .ConfigureAwait(false);
            if (alreadyRenewed) continue;

            await RenewSubscriptionAsync(subscription.Id).ConfigureAwait(false);
            renewed++;
        }

        var unpaidOverdue = await _db.Invoices.IgnoreQueryFilters()
            .Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue) && i.DueDate < now)
            .ToListAsync().ConfigureAwait(false);

        foreach (var invoice in unpaidOverdue)
        {
            if (invoice.Status == InvoiceStatus.Issued)
            {
                invoice.Status = InvoiceStatus.Overdue;
            }

            var subscription = await _db.Subscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId).ConfigureAwait(false);
            if (subscription == null || subscription.Status is SubscriptionStatus.Suspended or SubscriptionStatus.Canceled)
                continue;

            if (invoice.DueDate.AddDays(gracePeriodDays) < now)
            {
                subscription.Status = SubscriptionStatus.Suspended;
                suspended++;
            }
            else if (subscription.Status == SubscriptionStatus.Active)
            {
                subscription.Status = SubscriptionStatus.PastDue;
                pastDue++;
            }
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
        return (renewed, pastDue, suspended);
    }
}
