using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Memberships;
using Microsoft.Extensions.Caching.Memory;

namespace Inprem.Platform.Application.Memberships;

/// <summary>Validates the membership on every request (cached) and decides which one a sign-in uses.</summary>
public interface IMembershipService
{
    /// <summary>
    /// The membership a request may use: Active, the user's own, organization present. Cached for
    /// <see cref="MembershipService.CacheDuration"/>; call <see cref="Invalidate"/> whenever a
    /// membership changes so revocation takes effect on the next request.
    /// </summary>
    Task<ActiveMembershipInfo?> ValidateAsync(string userId, Guid membershipId, CancellationToken cancellationToken = default);

    void Invalidate(Guid membershipId);

    /// <summary>
    /// The user's Active memberships at sign-in. An account that belongs to an organization
    /// (AppUser.TenantId) but has no membership row at all gets an Active Admin membership
    /// there: admins created by the approved app's own pages keep working until those pages
    /// create memberships themselves (step 0.4). A revoked membership is never recreated.
    /// </summary>
    Task<IReadOnlyList<ActiveMembershipInfo>> GetForSignInAsync(string userId, int? accountTenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveMembershipInfo>> ListActiveAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class MembershipService : IMembershipService
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IMembershipStore _store;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;

    public MembershipService(IMembershipStore store, IMemoryCache cache, IClock clock)
    {
        _store = store;
        _cache = cache;
        _clock = clock;
    }

    private static string Key(Guid membershipId) => $"membership:{membershipId:N}";

    public async Task<ActiveMembershipInfo?> ValidateAsync(string userId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(Key(membershipId), out ActiveMembershipInfo? cached) && cached != null)
        {
            return cached.UserId == userId ? cached : null;
        }

        var membership = await _store.FindActiveAsync(membershipId, userId, cancellationToken).ConfigureAwait(false);
        if (membership != null)
        {
            _cache.Set(Key(membershipId), membership, CacheDuration);
        }
        return membership;
    }

    public void Invalidate(Guid membershipId) => _cache.Remove(Key(membershipId));

    public async Task<IReadOnlyList<ActiveMembershipInfo>> GetForSignInAsync(string userId, int? accountTenantId, CancellationToken cancellationToken = default)
    {
        var active = await _store.ListActiveAsync(userId, cancellationToken).ConfigureAwait(false);
        if (active.Count > 0 || accountTenantId is not { } tenantId)
        {
            return active;
        }

        if (await _store.ExistsAsync(userId, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return active;
        }

        await _store.AddAsync(TenantMembership.CreateActive(userId, tenantId, MembershipRole.Admin, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        return await _store.ListActiveAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ActiveMembershipInfo>> ListActiveAsync(string userId, CancellationToken cancellationToken = default) =>
        _store.ListActiveAsync(userId, cancellationToken);
}
