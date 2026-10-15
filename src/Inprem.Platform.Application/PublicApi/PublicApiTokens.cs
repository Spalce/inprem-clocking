using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.PublicApi;

namespace Inprem.Platform.Application.PublicApi;

public interface IPublicApiStore
{
    Task AddTokenAsync(ApiToken token, CancellationToken ct = default);
    Task<ApiToken?> FindTokenByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>The record for this scope and key (null when none, or only an expired one, which is then removed).</summary>
    Task<IdempotencyRecord?> FindIdempotencyAsync(string scope, string key, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Stores a new in-progress record; false if another request stored one first.</summary>
    Task<bool> TryAddIdempotencyAsync(IdempotencyRecord record, CancellationToken ct = default);

    Task RemoveIdempotencyAsync(IdempotencyRecord record, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public enum TokenCheck
{
    Valid,
    Missing,
    Invalid,
    Expired,
}

/// <summary>Issues and checks website API tokens (Phase 3 step 3.1). Plain tokens are returned once and never stored.</summary>
public sealed class PublicApiTokens
{
    private readonly IPublicApiStore _store;
    private readonly IClock _clock;

    public PublicApiTokens(IPublicApiStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<(ApiToken Token, string Value)> IssueSignupAsync(Guid signupId, int? tenantId, CancellationToken ct = default)
    {
        var value = SecretTokens.NewToken();
        var token = ApiToken.ForSignup(signupId, tenantId, SecretTokens.Hash(value), _clock.UtcNow);
        await _store.AddTokenAsync(token, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return (token, value);
    }

    public async Task<(ApiToken Token, string Value)> IssueBillingAccessAsync(int tenantId, CancellationToken ct = default)
    {
        var value = SecretTokens.NewToken();
        var token = ApiToken.ForBillingAccess(tenantId, SecretTokens.Hash(value), _clock.UtcNow);
        await _store.AddTokenAsync(token, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return (token, value);
    }

    /// <summary>Checks a bearer value. Expired and revoked tokens are told apart from unknown ones (token_expired vs token_invalid).</summary>
    public async Task<(TokenCheck Result, ApiToken? Token)> CheckAsync(string? value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return (TokenCheck.Missing, null);
        if (value.Length is < 20 or > 100) return (TokenCheck.Invalid, null);
        var token = await _store.FindTokenByHashAsync(SecretTokens.Hash(value), ct).ConfigureAwait(false);
        if (token == null) return (TokenCheck.Invalid, null);
        return token.IsValid(_clock.UtcNow) ? (TokenCheck.Valid, token) : (TokenCheck.Expired, token);
    }
}
