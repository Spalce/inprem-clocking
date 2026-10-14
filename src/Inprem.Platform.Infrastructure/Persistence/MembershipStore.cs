using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Memberships;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class MembershipStore : IMembershipStore
{
    private readonly PlatformDbContext _db;

    public MembershipStore(PlatformDbContext db) => _db = db;

    // Filter and sort on entity columns first, then build the records: EF Core cannot translate a
    // Where/OrderBy on the properties of a record created through its constructor.
    private IQueryable<ActiveRow> Rows(string userId, MembershipStatus status) =>
        from m in _db.TenantMemberships.AsNoTracking()
        join t in _db.Tenants.AsNoTracking() on m.TenantId equals t.Id
        where m.UserId == userId && m.Status == status
        select new ActiveRow { MembershipId = m.Id, UserId = m.UserId, TenantId = m.TenantId, TenantName = t.Name, Role = m.Role };

    private IQueryable<ActiveRow> Active(string userId) => Rows(userId, MembershipStatus.Active);

    public async Task<ActiveMembershipInfo?> FindActiveAsync(Guid membershipId, string userId, CancellationToken cancellationToken = default)
    {
        var row = await Active(userId).Where(m => m.MembershipId == membershipId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row?.ToInfo();
    }

    public async Task<IReadOnlyList<ActiveMembershipInfo>> ListActiveAsync(string userId, CancellationToken cancellationToken = default)
    {
        var rows = await Active(userId).OrderBy(m => m.TenantName).ThenBy(m => m.TenantId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => r.ToInfo()).ToList();
    }

    public async Task<IReadOnlyList<ActiveMembershipInfo>> ListInvitedAsync(string userId, CancellationToken cancellationToken = default)
    {
        var rows = await Rows(userId, MembershipStatus.Invited).OrderBy(m => m.TenantName).ThenBy(m => m.TenantId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => r.ToInfo()).ToList();
    }

    public Task<TenantMembership?> FindAsync(string userId, int tenantId, CancellationToken cancellationToken = default) =>
        _db.TenantMemberships.FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);

    public Task<TenantMembership?> FindByIdAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
        _db.TenantMemberships.FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken);

    public async Task<IReadOnlyList<MemberStatus>> ListMembersAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.TenantMemberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Select(m => new { m.UserId, m.Status }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new MemberStatus(r.UserId, r.Status)).ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);

    public async Task RecordSignInAsync(Guid membershipId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var membership = await _db.TenantMemberships.FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken).ConfigureAwait(false);
        if (membership == null) return;
        membership.RecordSignIn(nowUtc);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ActiveRow
    {
        public Guid MembershipId { get; init; }
        public string UserId { get; init; } = "";
        public int TenantId { get; init; }
        public string TenantName { get; init; } = "";
        public MembershipRole Role { get; init; }

        public ActiveMembershipInfo ToInfo() => new(MembershipId, UserId, TenantId, TenantName, Role);
    }

    public Task<bool> ExistsAsync(string userId, int tenantId, CancellationToken cancellationToken = default) =>
        _db.TenantMemberships.AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);

    public async Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
    {
        _db.TenantMemberships.Add(membership);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A concurrent sign-in already created this membership; the unique index is the guarantee.
            _db.Entry(membership).State = EntityState.Detached;
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
