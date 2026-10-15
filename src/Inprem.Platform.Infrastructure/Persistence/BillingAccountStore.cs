using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class BillingAccountStore : IBillingAccountStore
{
    private readonly PlatformDbContext _db;

    public BillingAccountStore(PlatformDbContext db) => _db = db;

    public async Task<IReadOnlyList<Payment>> PaymentsAsync(int tenantId, int max, CancellationToken ct = default) =>
        await _db.Payments.AsNoTracking().Where(p => p.TenantId == tenantId).OrderByDescending(p => p.PaidUtc).Take(max).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Plan>> PublishedPlansAsync(CancellationToken ct = default) =>
        (await _db.Plans.AsNoTracking().Include(p => p.Prices).Where(p => p.Status == PlanStatus.Active && p.IsPublic).OrderBy(p => p.Name).ToListAsync(ct).ConfigureAwait(false))
            .Where(p => p.Prices.Any(x => x.Status == PlanStatus.Active && x.PayPalPlanId != null)).ToList();

    public async Task<(IReadOnlyList<(Subscription Subscription, string TenantName)> Items, int Total)> ListAsync(SubscriptionStatus? status, BillingMode? mode, int skip, int take, CancellationToken ct = default)
    {
        var q = from s in _db.Subscriptions.AsNoTracking()
                join t in _db.Tenants.AsNoTracking() on s.TenantId equals t.Id
                select new { s, t.Name };
        if (status != null) q = q.Where(x => x.s.Status == status);
        if (mode != null) q = q.Where(x => x.s.Mode == mode);
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var rows = await q.OrderBy(x => x.Name).Skip(skip).Take(take).ToListAsync(ct).ConfigureAwait(false);
        return (rows.Select(r => (r.s, r.Name)).ToList(), total);
    }
}
