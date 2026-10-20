using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class ReconciliationStore : IReconciliationStore
{
    private readonly PlatformDbContext _db;

    public ReconciliationStore(PlatformDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> OpenCheckoutsAsync(int max, CancellationToken ct = default) =>
        await _db.CheckoutSessions.AsNoTracking()
            .Where(c => c.Status == CheckoutStatus.Created || c.Status == CheckoutStatus.AwaitingApproval || c.Status == CheckoutStatus.Approved)
            .OrderBy(c => c.CreatedUtc).Select(c => c.Id).Take(max).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> PayPalSubscriptionIdsAsync(CancellationToken ct = default) =>
        await _db.Subscriptions.AsNoTracking()
            .Where(s => s.Mode == BillingMode.PayPal && s.ExternalSubscriptionId != null && s.Status != SubscriptionStatus.Closed)
            .OrderBy(s => s.TenantId).Select(s => s.ExternalSubscriptionId!).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<int>> EndedTrialsAsync(DateTime nowUtc, CancellationToken ct = default) =>
        await _db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsUtc != null && s.TrialEndsUtc <= nowUtc)
            .OrderBy(s => s.TenantId).Select(s => s.TenantId).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<(int TenantId, DateTime TrialEndsUtc)>> TrialsEndingAsync(DateTime afterUtc, DateTime untilUtc, CancellationToken ct = default) =>
        (await _db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsUtc != null && s.TrialEndsUtc > afterUtc && s.TrialEndsUtc <= untilUtc)
            .OrderBy(s => s.TenantId).Select(s => new { s.TenantId, s.TrialEndsUtc }).ToListAsync(ct).ConfigureAwait(false))
        .Select(s => (s.TenantId, s.TrialEndsUtc!.Value)).ToList();

    public async Task<IReadOnlyList<OverdueInvoice>> OverdueInvoicesAsync(DateOnly today, CancellationToken ct = default) =>
        await _db.Invoices.AsNoTracking()
            .Where(i => (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate < today)
            .OrderBy(i => i.DueDate)
            .Select(i => new OverdueInvoice(i.Id, i.TenantId, i.Number, i.Total - i.PaidAmount, i.Currency, i.DueDate))
            .ToListAsync(ct).ConfigureAwait(false);
}
