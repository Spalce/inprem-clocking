using Inprem.Platform.Application.PublicApi;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.PublicApi;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class BillingAccessStore : IBillingAccessStore
{
    private readonly PlatformDbContext _db;

    public BillingAccessStore(PlatformDbContext db) => _db = db;

    public async Task<IReadOnlyList<(int TenantId, string Name)>> OwnedOrganizationsAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var rows = await (from a in _db.Accounts.AsNoTracking()
                          join m in _db.TenantMemberships.AsNoTracking() on a.Id equals m.UserId
                          join t in _db.Tenants.AsNoTracking() on m.TenantId equals t.Id
                          where a.Email != null && a.Email.ToUpper() == normalized && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Active
                          orderby t.Name
                          select new { m.TenantId, t.Name }).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => (r.TenantId, r.Name)).ToList();
    }

    public Task<int> CodesSinceAsync(int tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
        _db.BillingAccessCodes.CountAsync(c => c.TenantId == tenantId && c.CreatedUtc >= sinceUtc, ct);

    public async Task AddCodeAsync(BillingAccessCode code, CancellationToken ct = default) => await _db.BillingAccessCodes.AddAsync(code, ct).ConfigureAwait(false);

    public Task<BillingAccessCode?> FindCodeByHashAsync(string codeHash, CancellationToken ct = default) => _db.BillingAccessCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash, ct);

    public async Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
