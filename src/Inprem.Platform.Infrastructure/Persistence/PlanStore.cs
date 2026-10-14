using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class PlanStore : IPlanStore
{
    private readonly PlatformDbContext _db;

    public PlanStore(PlatformDbContext db) => _db = db;

    public async Task<IReadOnlyList<Plan>> ListAsync(string? search, PlanStatus? status, CancellationToken ct = default)
    {
        var query = _db.Plans.Include(p => p.Prices).AsQueryable();
        if (status != null) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Code.Contains(term));
        }
        return await query.OrderBy(p => p.Status).ThenBy(p => p.Name).ToListAsync(ct).ConfigureAwait(false);
    }

    public Task<Plan?> FindAsync(Guid id, CancellationToken ct = default) =>
        _db.Plans.Include(p => p.Prices).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) => _db.Plans.AnyAsync(p => p.Code == code, ct);

    public Task<IReadOnlyDictionary<Guid, int>> CountSubscriptionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

    public async Task AddAsync(Plan plan, CancellationToken ct = default) => await _db.Plans.AddAsync(plan, ct).ConfigureAwait(false);

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
