using Inprem.Platform.Application.PublicApi;
using Inprem.Platform.Domain.PublicApi;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class PublicApiStore : IPublicApiStore
{
    private readonly PlatformDbContext _db;

    public PublicApiStore(PlatformDbContext db) => _db = db;

    public async Task AddTokenAsync(ApiToken token, CancellationToken ct = default) => await _db.ApiTokens.AddAsync(token, ct).ConfigureAwait(false);

    public Task<ApiToken?> FindTokenByHashAsync(string tokenHash, CancellationToken ct = default) => _db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IdempotencyRecord?> FindIdempotencyAsync(string scope, string key, DateTime nowUtc, CancellationToken ct = default)
    {
        var record = await _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Scope == scope && r.Key == key, ct).ConfigureAwait(false);
        if (record == null || record.ExpiresUtc > nowUtc) return record;
        _db.IdempotencyRecords.Remove(record); // older than 24 hours: the key is free again
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }

    public async Task<bool> TryAddIdempotencyAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        await _db.IdempotencyRecords.AddAsync(record, ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2627 or 2601 })
        {
            _db.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public async Task RemoveIdempotencyAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        _db.IdempotencyRecords.Remove(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
