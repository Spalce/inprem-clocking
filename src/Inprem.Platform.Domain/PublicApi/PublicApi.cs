namespace Inprem.Platform.Domain.PublicApi;

public enum ApiTokenKind
{
    /// <summary>From <c>POST /signups</c>: that signup and the checkouts it created; 2 hours.</summary>
    Signup,

    /// <summary>From an emailed one-time code: billing of one organization; 30 minutes.</summary>
    BillingAccess,
}

/// <summary>
/// A website API token (Phase 3 step 3.1, design 7.2). The token itself is a random 32-byte value
/// sent as <c>Authorization: Bearer</c>; only its SHA-256 hash is stored.
/// </summary>
public sealed class ApiToken
{
    public static readonly TimeSpan SignupLifetime = TimeSpan.FromHours(2);
    public static readonly TimeSpan BillingAccessLifetime = TimeSpan.FromMinutes(30);

    private ApiToken()
    {
        TokenHash = "";
    }

    public Guid Id { get; private set; }
    public ApiTokenKind Kind { get; private set; }
    public string TokenHash { get; private set; }

    /// <summary>The signup a signup token belongs to.</summary>
    public Guid? SignupId { get; private set; }

    /// <summary>The organization: always for billing access; for a signup token once its organization exists.</summary>
    public int? TenantId { get; private set; }

    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }

    public static ApiToken ForSignup(Guid signupId, int? tenantId, string tokenHash, DateTime nowUtc) =>
        Create(ApiTokenKind.Signup, signupId, tenantId, tokenHash, nowUtc, SignupLifetime);

    public static ApiToken ForBillingAccess(int tenantId, string tokenHash, DateTime nowUtc) =>
        Create(ApiTokenKind.BillingAccess, null, tenantId, tokenHash, nowUtc, BillingAccessLifetime);

    private static ApiToken Create(ApiTokenKind kind, Guid? signupId, int? tenantId, string tokenHash, DateTime nowUtc, TimeSpan lifetime)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (tokenHash.Length != 64) throw new ArgumentException("A SHA-256 hex hash is expected.", nameof(tokenHash));
        if (kind == ApiTokenKind.Signup && signupId == null) throw new ArgumentException("A signup token needs its signup.", nameof(signupId));
        if (kind == ApiTokenKind.BillingAccess && tenantId == null) throw new ArgumentException("A billing access token needs its organization.", nameof(tenantId));
        return new ApiToken
        {
            Id = Guid.NewGuid(), Kind = kind, TokenHash = tokenHash, SignupId = signupId, TenantId = tenantId,
            CreatedUtc = nowUtc, ExpiresUtc = nowUtc + lifetime,
        };
    }

    public bool IsValid(DateTime nowUtc) => RevokedUtc == null && nowUtc < ExpiresUtc;

    public void Revoke(DateTime nowUtc) => RevokedUtc ??= nowUtc;

    /// <summary>A signup token learns its organization once it exists.</summary>
    public void LinkTenant(int tenantId)
    {
        if (TenantId != null && TenantId != tenantId) throw new InvalidOperationException("This token already belongs to another organization.");
        TenantId = tenantId;
    }
}

/// <summary>
/// The stored answer to a request sent with an <c>Idempotency-Key</c> (Phase 3 step 3.1, design
/// 7.7): the same key and body within 24 hours get the same answer; the same key with another body
/// is refused. A record without a status is a request still being processed.
/// </summary>
public sealed class IdempotencyRecord
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    public const int MaxBodyLength = 64 * 1024;

    private IdempotencyRecord()
    {
        Scope = "";
        Key = "";
        RequestHash = "";
    }

    /// <summary>The endpoint and who called it (token id, or "public").</summary>
    public string Scope { get; private set; }
    public string Key { get; private set; }

    /// <summary>SHA-256 of the request body.</summary>
    public string RequestHash { get; private set; }

    /// <summary>Null while the first request is still running.</summary>
    public int? StatusCode { get; private set; }
    public string? ContentType { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }

    public bool IsComplete => StatusCode != null;

    public static IdempotencyRecord Begin(string scope, string key, string requestHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 200) throw new ArgumentException("Scope required (at most 200 characters).", nameof(scope));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ArgumentException("Key required (at most 100 characters).", nameof(key));
        return new IdempotencyRecord { Scope = scope, Key = key, RequestHash = requestHash, CreatedUtc = nowUtc, ExpiresUtc = nowUtc + Lifetime };
    }

    public void Complete(int statusCode, string? contentType, string? body)
    {
        if (body?.Length > MaxBodyLength) throw new ArgumentException("The response is too large to keep.", nameof(body));
        StatusCode = statusCode;
        ContentType = contentType;
        ResponseBody = body;
    }
}
