using Inprem.Platform.Application.Support;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class SupportSessionStore : ISupportSessionStore
{
    private readonly PlatformDbContext _db;

    public SupportSessionStore(PlatformDbContext db) => _db = db;

    public async Task AddAsync(SupportSession session, CancellationToken ct = default) => await _db.SupportSessions.AddAsync(session, ct).ConfigureAwait(false);

    public Task<SupportSession?> FindAsync(Guid id, CancellationToken ct = default) => _db.SupportSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<SupportSession?> FindByHandoffHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.SupportSessions.FirstOrDefaultAsync(s => s.HandoffTokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<SupportSession>> ListForTenantAsync(int tenantId, CancellationToken ct = default) =>
        await _db.SupportSessions.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartedUtc).Take(200).ToListAsync(ct).ConfigureAwait(false);

    public async Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
