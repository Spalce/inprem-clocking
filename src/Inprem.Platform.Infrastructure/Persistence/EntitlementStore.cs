using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class EntitlementStore : IEntitlementStore
{
    private readonly PlatformDbContext _db;

    public EntitlementStore(PlatformDbContext db) => _db = db;

    public async Task<(Subscription Subscription, int? PlanMaxPeople)?> FindAsync(int tenantId, CancellationToken ct = default)
    {
        var row = await (from s in _db.Subscriptions.AsNoTracking()
                         where s.TenantId == tenantId
                         join p in _db.Plans.AsNoTracking() on s.PlanId equals p.Id into plans
                         from p in plans.DefaultIfEmpty()
                         select new { Subscription = s, MaxPeople = p == null ? null : p.MaxPeople }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return row == null ? null : (row.Subscription, row.MaxPeople);
    }

    public Task<bool> IsSuspendedAsync(int tenantId, CancellationToken ct = default) =>
        _db.TenantProfiles.AsNoTracking().AnyAsync(p => p.TenantId == tenantId && p.Status == Inprem.Platform.Domain.Organizations.OrganizationStatus.Suspended, ct);
}
