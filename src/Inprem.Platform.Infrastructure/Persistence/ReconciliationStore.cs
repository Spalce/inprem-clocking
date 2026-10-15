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
}
