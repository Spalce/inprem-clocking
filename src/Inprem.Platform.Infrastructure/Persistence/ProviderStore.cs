using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Provider;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class ProviderStore : IProviderStore
{
    private readonly PlatformDbContext _db;

    public ProviderStore(PlatformDbContext db) => _db = db;

    public Task<ProviderUser?> FindProviderUserAsync(string userId, CancellationToken ct = default) =>
        _db.ProviderUsers.FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public Task<bool> AnyOwnerAsync(DateTime nowUtc, CancellationToken ct = default) =>
        _db.ProviderUsers.AnyAsync(u => u.Role == ProviderRole.Owner &&
            (u.Status == ProviderUserStatus.Active ||
             (u.Status == ProviderUserStatus.Invited && _db.ProviderInvitations.Any(i =>
                 i.UserId == u.UserId && i.ConsumedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > nowUtc))), ct);

    public async Task<IReadOnlyList<OwnerInfo>> ListActiveOwnersAsync(CancellationToken ct = default)
    {
        var rows = await (from u in _db.ProviderUsers.AsNoTracking()
                          join a in _db.Accounts.AsNoTracking() on u.UserId equals a.Id
                          where u.Role == ProviderRole.Owner && u.Status == ProviderUserStatus.Active
                          orderby a.Email
                          select new { a.Id, a.Email, a.TwoFactorEnabled }).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new OwnerInfo(r.Id, r.Email ?? "", r.TwoFactorEnabled)).ToList();
    }

    public async Task AddProviderUserAsync(ProviderUser user, CancellationToken ct = default) =>
        await _db.ProviderUsers.AddAsync(user, ct).ConfigureAwait(false);

    public async Task AddInvitationAsync(Invitation invitation, CancellationToken ct = default) =>
        await _db.ProviderInvitations.AddAsync(invitation, ct).ConfigureAwait(false);

    public Task<Invitation?> FindInvitationByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.ProviderInvitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<Invitation>> ListPendingInvitationsAsync(string userId, DateTime nowUtc, CancellationToken ct = default) =>
        await _db.ProviderInvitations.Where(i => i.UserId == userId && i.ConsumedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > nowUtc)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task AddRecoveryAsync(RecoveryRecord record, CancellationToken ct = default) =>
        await _db.RecoveryRecords.AddAsync(record, ct).ConfigureAwait(false);

    public Task<RecoveryRecord?> FindRecoveryAsync(Guid id, CancellationToken ct = default) =>
        _db.RecoveryRecords.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<RecoveryRecord?> FindRecoveryByApprovalHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RecoveryRecords.FirstOrDefaultAsync(r => r.FirstApprovalTokenHash == tokenHash || r.SecondApprovalTokenHash == tokenHash, ct);

    public Task<bool> AnyRecoverySinceAsync(string targetUserId, DateTime sinceUtc, CancellationToken ct = default) =>
        _db.RecoveryRecords.AnyAsync(r => r.TargetUserId == targetUserId && r.CreatedUtc > sinceUtc, ct);

    public Task<bool> IsCodeUsedAsync(string codeHash, CancellationToken ct = default) =>
        _db.UsedCodes.AnyAsync(c => c.CodeHash == codeHash, ct);

    public async Task MarkCodeUsedAsync(string codeHash, string purpose, DateTime nowUtc, CancellationToken ct = default) =>
        await _db.UsedCodes.AddAsync(new UsedCode { CodeHash = codeHash, Purpose = purpose, UsedUtc = nowUtc }, ct).ConfigureAwait(false);

    public async Task ReleaseCodeAsync(string codeHash, CancellationToken ct = default)
    {
        var used = await _db.UsedCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash, ct).ConfigureAwait(false);
        if (used != null) _db.UsedCodes.Remove(used);
    }

    public async Task AddAttemptAsync(string kind, bool succeeded, string source, DateTime nowUtc, CancellationToken ct = default) =>
        await _db.SecurityAttempts.AddAsync(new SecurityAttempt
        {
            Kind = kind, Succeeded = succeeded, Source = source.Length > 300 ? source[..300] : source, OccurredUtc = nowUtc,
        }, ct).ConfigureAwait(false);

    public Task<int> CountFailuresSinceAsync(string kind, DateTime sinceUtc, CancellationToken ct = default) =>
        _db.SecurityAttempts.CountAsync(a => a.Kind == kind && !a.Succeeded && a.OccurredUtc > sinceUtc, ct);

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) =>
        await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
