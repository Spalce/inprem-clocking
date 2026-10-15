using Inprem.Platform.Application.PublicApi;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Organizations;
using Inprem.Platform.Domain.PublicApi;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class SignupStore : ISignupStore
{
    private readonly PlatformDbContext _db;

    public SignupStore(PlatformDbContext db) => _db = db;

    public Task<Signup?> FindAsync(Guid signupId, CancellationToken ct = default) => _db.Signups.FirstOrDefaultAsync(s => s.Id == signupId, ct);

    public Task<TenantProfile?> FindProfileAsync(int tenantId, CancellationToken ct = default) => _db.TenantProfiles.FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public Task<Subscription?> FindSubscriptionAsync(int tenantId, CancellationToken ct = default) => _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
