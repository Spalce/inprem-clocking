using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class BillingEventStore : IBillingEventStore
{
    private readonly PlatformDbContext _db;

    public BillingEventStore(PlatformDbContext db) => _db = db;

    public async Task<bool> TryAddAsync(BillingEvent billingEvent, CancellationToken ct = default)
    {
        if (await _db.BillingEvents.AsNoTracking().AnyAsync(e => e.ExternalEventId == billingEvent.ExternalEventId, ct).ConfigureAwait(false)) return false;
        await _db.BillingEvents.AddAsync(billingEvent, ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2627 or 2601 })
        {
            // The same event delivered twice at the same moment: the primary key keeps one.
            _db.Entry(billingEvent).State = EntityState.Detached;
            return false;
        }
    }

    public Task<BillingEvent?> FindAsync(string externalEventId, CancellationToken ct = default) => _db.BillingEvents.FirstOrDefaultAsync(e => e.ExternalEventId == externalEventId, ct);

    public async Task<(IReadOnlyList<BillingEvent> Items, int Total)> ListAsync(BillingEventStatus? status, string? eventType, int skip, int take, CancellationToken ct = default)
    {
        var q = _db.BillingEvents.AsNoTracking().AsQueryable();
        q = status switch
        {
            BillingEventStatus.Processed => q.Where(e => e.ProcessedUtc != null),
            BillingEventStatus.Failed => q.Where(e => e.ProcessedUtc == null && e.Error != null),
            BillingEventStatus.Received => q.Where(e => e.ProcessedUtc == null && e.Error == null),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(eventType)) q = q.Where(e => e.EventType.StartsWith(eventType.Trim()));
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await q.OrderByDescending(e => e.ReceivedUtc).Skip(skip).Take(take).ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
