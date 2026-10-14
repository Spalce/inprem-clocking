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

    /// <summary>Makes a newly created account an Active member (an admin added on the Admins page).</summary>
    Task AddMemberAsync(string userId, int tenantId, MembershipRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invites an existing account to the organization (C3-6). An Active member is not invited
    /// again; a pending invitation is left as it is; a revoked or declined one is renewed.
    /// </summary>
    Task<InviteOutcome> InviteAsync(string userId, int tenantId, MembershipRole role, string invitedByUserId, CancellationToken cancellationToken = default);

    /// <summary>The user's pending invitations (Status Invited), with organization names.</summary>
    Task<IReadOnlyList<ActiveMembershipInfo>> ListInvitationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Every membership of the organization, as account id and status (for the Admins page).</summary>
    Task<IReadOnlyList<MemberStatus>> ListMembersAsync(int tenantId, CancellationToken cancellationToken = default);

    /// <summary>Accepts one of the user's own pending invitations. False when it is not theirs or not pending.</summary>
    Task<bool> AcceptAsync(string userId, Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>Declines one of the user's own pending invitations. False when it is not theirs or not pending.</summary>
    Task<bool> DeclineAsync(string userId, Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>Records that the membership was used to sign in now.</summary>
    Task RecordSignInAsync(Guid membershipId, CancellationToken cancellationToken = default);
}

public enum InviteOutcome
{
    /// <summary>A new invitation was created, or a revoked membership was invited again.</summary>
    Invited,

    /// <summary>An invitation was already pending; nothing changed.</summary>
    AlreadyInvited,

    /// <summary>The person is already an Active member; nothing changed.</summary>
    AlreadyMember,
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

    public Task AddMemberAsync(string userId, int tenantId, MembershipRole role, CancellationToken cancellationToken = default) =>
        _store.AddAsync(TenantMembership.CreateActive(userId, tenantId, role, _clock.UtcNow), cancellationToken);

    public async Task<InviteOutcome> InviteAsync(string userId, int tenantId, MembershipRole role, string invitedByUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _store.FindAsync(userId, tenantId, cancellationToken).ConfigureAwait(false);
        switch (existing?.Status)
        {
            case null:
                await _store.AddAsync(TenantMembership.CreateInvited(userId, tenantId, role, invitedByUserId, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
                return InviteOutcome.Invited;
            case MembershipStatus.Active:
                return InviteOutcome.AlreadyMember;
            case MembershipStatus.Invited:
                return InviteOutcome.AlreadyInvited;
            default:
                existing.Reinvite(invitedByUserId);
                existing.ChangeRole(role);
                await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                Invalidate(existing.Id);
                return InviteOutcome.Invited;
        }
    }

    public Task<IReadOnlyList<ActiveMembershipInfo>> ListInvitationsAsync(string userId, CancellationToken cancellationToken = default) =>
        _store.ListInvitedAsync(userId, cancellationToken);

    public Task<IReadOnlyList<MemberStatus>> ListMembersAsync(int tenantId, CancellationToken cancellationToken = default) =>
        _store.ListMembersAsync(tenantId, cancellationToken);

    public Task RecordSignInAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
        _store.RecordSignInAsync(membershipId, _clock.UtcNow, cancellationToken);

    public Task<bool> AcceptAsync(string userId, Guid membershipId, CancellationToken cancellationToken = default) =>
        ChangeInvitationAsync(userId, membershipId, m => m.Activate(), cancellationToken);

    public Task<bool> DeclineAsync(string userId, Guid membershipId, CancellationToken cancellationToken = default) =>
        ChangeInvitationAsync(userId, membershipId, m => m.Decline(), cancellationToken);

    private async Task<bool> ChangeInvitationAsync(string userId, Guid membershipId, Action<TenantMembership> change, CancellationToken cancellationToken)
    {
        var membership = await _store.FindByIdAsync(membershipId, cancellationToken).ConfigureAwait(false);
        if (membership == null || membership.UserId != userId || membership.Status != MembershipStatus.Invited)
        {
            return false;
        }

        change(membership);
        await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(membership.Id);
        return true;
    }
}
