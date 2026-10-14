using Inprem.Platform.Application.Audit;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class AuditReader : IAuditReader
{
    private readonly PlatformDbContext _db;

    public AuditReader(PlatformDbContext db) => _db = db;

    public async Task<(IReadOnlyList<AuditEntry> Items, int Total)> QueryAsync(AuditQuery query, int skip, int? take, CancellationToken ct = default)
    {
        var q = _db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Actor)) q = q.Where(a => a.Actor == query.Actor);
        if (query.TenantId != null) q = q.Where(a => a.TenantId == query.TenantId);
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            q = q.Where(a => a.Action.StartsWith(action));
        }
        if (query.From != null) q = q.Where(a => a.OccurredUtc >= query.From.Value.Date);
        if (query.To != null) q = q.Where(a => a.OccurredUtc < query.To.Value.Date.AddDays(1));

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var ordered = q.OrderByDescending(a => a.Id).Skip(skip);
        var items = await (take == null ? ordered : ordered.Take(take.Value)).ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task<IReadOnlyList<string>> ActionsAsync(CancellationToken ct = default) =>
        await _db.AuditEntries.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<int, string>> TenantNamesAsync(CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct).ConfigureAwait(false);

    public async Task<DashboardFigures> DashboardAsync(DateTime nowUtc, TimeSpan trialWindow, CancellationToken ct = default)
    {
        // Organizations without a profile yet (created before the console) count as their own
        // status: active unless they were made inactive.
        var tenants = await _db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        var profiles = await _db.TenantProfiles.AsNoTracking().ToDictionaryAsync(p => p.TenantId, p => p.Status, ct).ConfigureAwait(false);
        var byStatus = tenants
            .GroupBy(id => (profiles.TryGetValue(id, out var status) ? status : OrganizationStatus.Active).ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var subscriptions = await _db.Subscriptions.AsNoTracking().GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        var until = nowUtc + trialWindow;
        var trials = await (from s in _db.Subscriptions.AsNoTracking()
                            join t in _db.Tenants.AsNoTracking() on s.TenantId equals t.Id
                            where s.Status == SubscriptionStatus.Trialing && s.TrialEndsUtc != null && s.TrialEndsUtc <= until
                            orderby s.TrialEndsUtc
                            select new { s.TenantId, t.Name, Ends = s.TrialEndsUtc!.Value }).Take(20).ToListAsync(ct).ConfigureAwait(false);
        var recent = await _db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Id).Take(10).ToListAsync(ct).ConfigureAwait(false);

        return new DashboardFigures(byStatus, subscriptions.ToDictionary(s => s.Key, s => s.Count),
            trials.Select(t => (t.TenantId, t.Name, t.Ends)).ToList(), recent);
    }
}
