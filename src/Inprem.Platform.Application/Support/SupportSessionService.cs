using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Domain.Support;

namespace Inprem.Platform.Application.Support;

public interface ISupportSessionStore
{
    Task AddAsync(SupportSession session, CancellationToken ct = default);
    Task<SupportSession?> FindAsync(Guid id, CancellationToken ct = default);
    Task<SupportSession?> FindByHandoffHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<SupportSession>> ListForTenantAsync(int tenantId, CancellationToken ct = default);
    Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default);
    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record SupportStart(bool Succeeded, string? Error, Guid? SessionId = null, string? HandoffUrl = null);

/// <summary>A redeemed or validated session: who works in which organization until when.</summary>
public sealed record ActiveSupport(Guid SessionId, string ProviderUserId, string ProviderEmail, int TenantId, string TenantName, DateTime ExpiresUtc);

/// <summary>
/// Support sessions (Phase 1 step 1.5). Starting one needs an Owner or Support provider user and a
/// reason; the hand-off link is single-use and lives 60 seconds (only its hash is stored). Every
/// request in the session is audited by the App site; billing is blocked there.
/// </summary>
public sealed class SupportSessionService
{
    public const string HandoffPath = "/Account/Support/Handoff";

    private readonly ISupportSessionStore _store;
    private readonly IProviderStore _provider;
    private readonly IAccountGateway _accounts;
    private readonly IClock _clock;

    public SupportSessionService(ISupportSessionStore store, IProviderStore provider, IAccountGateway accounts, IClock clock)
    {
        _store = store;
        _provider = provider;
        _accounts = accounts;
        _clock = clock;
    }

    public Task<IReadOnlyList<SupportSession>> ListForTenantAsync(int tenantId, CancellationToken ct = default) => _store.ListForTenantAsync(tenantId, ct);

    public async Task<SupportStart> StartAsync(string providerUserId, int tenantId, string reason, int minutes, string? appLinkBase, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var user = await _provider.FindProviderUserAsync(providerUserId, ct).ConfigureAwait(false);
        if (user is not { Status: ProviderUserStatus.Active } || user.Role == ProviderRole.Billing)
            return new SupportStart(false, "Only active Provider Owners and Support users can start support sessions.");
        var tenantName = await _store.TenantNameAsync(tenantId, ct).ConfigureAwait(false);
        if (tenantName == null) return new SupportStart(false, "No such organization.");
        if (string.IsNullOrWhiteSpace(appLinkBase)) return new SupportStart(false, "Links:AppBaseUrl is not configured, so the App site cannot be opened.");
        var email = (await _accounts.FindByIdAsync(providerUserId, ct).ConfigureAwait(false))?.Email ?? providerUserId;

        var token = SecretTokens.NewToken();
        SupportSession session;
        try
        {
            session = SupportSession.Start(tenantId, providerUserId, email, reason, minutes, SecretTokens.Hash(token), now);
        }
        catch (ArgumentException e)
        {
            return new SupportStart(false, e.ParamName == null ? e.Message : e.Message.Replace($" (Parameter '{e.ParamName}')", ""));
        }

        await _store.AddAsync(session, ct).ConfigureAwait(false);
        await _store.AddAuditAsync(AuditEntry.Record($"provider:{providerUserId}", "support.started", session.Id.ToString(),
            $"{email} in {tenantName} for {minutes} minutes. Reason: {session.Reason}", now, tenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SupportStart(true, null, session.Id, $"{appLinkBase.TrimEnd('/')}{HandoffPath}?token={Uri.EscapeDataString(token)}");
    }

    /// <summary>What a hand-off link would open (for the confirmation page), without using it.</summary>
    public async Task<ActiveSupport?> PeekAsync(string token, CancellationToken ct = default)
    {
        var session = string.IsNullOrWhiteSpace(token) ? null : await _store.FindByHandoffHashAsync(SecretTokens.Hash(token), ct).ConfigureAwait(false);
        return session != null && session.CanRedeem(_clock.UtcNow) ? await ToActiveAsync(session, ct).ConfigureAwait(false) : null;
    }

    /// <summary>Uses the hand-off link (once). Null when it is unknown, used, expired, or the provider user can no longer act.</summary>
    public async Task<ActiveSupport?> RedeemAsync(string token, string source, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var session = string.IsNullOrWhiteSpace(token) ? null : await _store.FindByHandoffHashAsync(SecretTokens.Hash(token), ct).ConfigureAwait(false);
        if (session == null || !session.CanRedeem(now) || !await CanActAsync(session, ct).ConfigureAwait(false)) return null;

        session.Redeem(now);
        await _store.AddAuditAsync(AuditEntry.Record($"provider:{session.ProviderUserId}", "support.handoff", session.Id.ToString(), $"Opened the App site from {source}", now, session.TenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ToActiveAsync(session, ct).ConfigureAwait(false);
    }

    /// <summary>The session behind a signed-in support request, when it is still active (else null; the caller signs out).</summary>
    public async Task<ActiveSupport?> ValidateAsync(Guid sessionId, string userId, int tenantId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var session = await _store.FindAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null || session.ProviderUserId != userId || session.TenantId != tenantId || session.HandoffUsedUtc == null) return null;
        if (!session.IsActive(now) || !await CanActAsync(session, ct).ConfigureAwait(false))
        {
            if (session.EndedUtc == null)
            {
                session.End(session.IsActive(now) ? "provider user no longer allowed" : "expired", now);
                await _store.AddAuditAsync(AuditEntry.Record("system", "support.ended", session.Id.ToString(), $"Ended: {session.EndedBy}", now, session.TenantId), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return null;
        }
        return await ToActiveAsync(session, ct).ConfigureAwait(false);
    }

    /// <summary>One audited request of a support session (method, path; never bodies).</summary>
    public async Task RecordRequestAsync(ActiveSupport support, string method, string path, bool blocked, CancellationToken ct = default)
    {
        await _store.AddAuditAsync(AuditEntry.Record($"provider:{support.ProviderUserId}", blocked ? "support.request-blocked" : "support.request",
            support.SessionId.ToString(), $"{method} {(path.Length > 400 ? path[..400] : path)}", _clock.UtcNow, support.TenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Ends a session: by the person in it (App site) or by an Owner/Support user in the console.</summary>
    public async Task<bool> EndAsync(Guid sessionId, string actor, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var session = await _store.FindAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null || !session.IsActive(now)) return false;
        session.End(actor, now);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "support.ended", session.Id.ToString(), $"Ended by {actor}", now, session.TenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The provider user must still be active (not disabled), Owner or Support.</summary>
    private async Task<bool> CanActAsync(SupportSession session, CancellationToken ct) =>
        await _provider.FindProviderUserAsync(session.ProviderUserId, ct).ConfigureAwait(false) is { Status: ProviderUserStatus.Active } user
        && user.Role != ProviderRole.Billing;

    private async Task<ActiveSupport> ToActiveAsync(SupportSession session, CancellationToken ct) =>
        new(session.Id, session.ProviderUserId, session.ProviderEmail, session.TenantId,
            await _store.TenantNameAsync(session.TenantId, ct).ConfigureAwait(false) ?? $"organization {session.TenantId}", session.ExpiresUtc);
}
