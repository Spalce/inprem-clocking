using System.Data;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Memberships;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class CheckoutStore : ICheckoutStore
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    private readonly PlatformDbContext _db;

    public CheckoutStore(PlatformDbContext db) => _db = db;

    public async Task AddAsync(CheckoutSession session, CancellationToken ct = default) => await _db.CheckoutSessions.AddAsync(session, ct).ConfigureAwait(false);

    public Task<CheckoutSession?> FindAsync(Guid id, CancellationToken ct = default) => _db.CheckoutSessions.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CheckoutSession?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default) => _db.CheckoutSessions.FirstOrDefaultAsync(c => c.IdempotencyKey == key, ct);

    public Task<CheckoutSession?> FindByExternalIdAsync(string externalSubscriptionId, CancellationToken ct = default) =>
        _db.CheckoutSessions.Where(c => c.ExternalSubscriptionId == externalSubscriptionId).OrderByDescending(c => c.CreatedUtc).FirstOrDefaultAsync(ct);

    public Task<Subscription?> FindSubscriptionAsync(int tenantId, CancellationToken ct = default) => _db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task<int?> TenantWithExternalIdAsync(string externalSubscriptionId, CancellationToken ct = default) =>
        await _db.Subscriptions.AsNoTracking().Where(s => s.ExternalSubscriptionId == externalSubscriptionId).Select(s => (int?)s.TenantId).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<(Plan Plan, PlanPrice Price)?> FindPriceAsync(Guid priceId, CancellationToken ct = default)
    {
        var plan = await _db.Plans.Include(p => p.Prices).FirstOrDefaultAsync(p => p.Prices.Any(x => x.Id == priceId), ct).ConfigureAwait(false);
        return plan == null ? null : (plan, plan.Prices.Single(p => p.Id == priceId));
    }

    public async Task<IReadOnlyList<string>> OwnerEmailsAsync(int tenantId, CancellationToken ct = default) =>
        await (from m in _db.TenantMemberships.AsNoTracking()
               join a in _db.Accounts.AsNoTracking() on m.UserId equals a.Id
               where m.TenantId == tenantId && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Active && a.Email != null
               select a.Email!).Distinct().ToListAsync(ct).ConfigureAwait(false);

    public async Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<T> WithLockAsync<T>(string key, Func<Task<T>> work, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction != null) return await work().ConfigureAwait(false); // already inside a lock of ours

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await _db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
            new object[] { result, new SqlParameter("@resource", $"inprem:{key}"), new SqlParameter("@timeout", (int)LockTimeout.TotalMilliseconds) }, ct).ConfigureAwait(false);
        if ((int)result.Value < 0) throw new TimeoutException($"Could not lock {key} within {LockTimeout.TotalSeconds} seconds.");

        var value = await work().ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false); // releases the lock
        return value;
    }

    public void Reset() => _db.ChangeTracker.Clear();

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
